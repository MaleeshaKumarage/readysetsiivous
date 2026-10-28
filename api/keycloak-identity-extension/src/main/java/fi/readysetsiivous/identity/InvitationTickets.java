package fi.readysetsiivous.identity;

import java.util.*;
import org.keycloak.TokenVerifier;
import org.keycloak.authentication.AuthenticatorUtil;
import org.keycloak.authentication.actiontoken.execactions.ExecuteActionsActionToken;
import org.keycloak.common.util.Time;
import org.keycloak.models.*;
import org.keycloak.representations.JsonWebToken;
import org.keycloak.sessions.AuthenticationSessionModel;
import org.keycloak.userprofile.UserProfileProvider;
import org.keycloak.services.managers.AuthenticationManager;

/** Cache removal is immediate/atomic in pinned Infinispan; put is transaction queued. */
final class InvitationTickets {
    static final String ATTRIBUTE = "cleaning_invitation_ticket";
    static final String EPOCH = "cleaning_invitation_epoch";
    static final String MARKER = "cleaning.invitation.guarded";
    static final String PREFIX = "cleaning.invitation.";
    static final String ORDINARY = "cleaning.password.challenge";
    static final List<String> FIELDS = List.of("realm", "user", "owner", "email", "client", "expires", "ticket", "epoch");
    private final CompleteIdentityPolicy policy;
    private final KeycloakSession session;
    private final RealmModel realm;
    InvitationTickets(KeycloakSession session, RealmModel realm) {
        this.policy = new CompleteIdentityPolicy(session,realm); this.session=session; this.realm=realm;
    }
    private static String key(RealmModel realm, String ticket) { return "cleaning-invitation:" + realm.getId() + ":" + ticket; }
    private void protectedMetadata() {
        var config = session.getProvider(UserProfileProvider.class).getConfiguration();
        var fields = config.getAttributes().stream().filter(a -> ATTRIBUTE.equals(a.getName())).toList();
        if (fields.size()!=1 || fields.getFirst().getPermissions()==null
                || !Set.of("admin").equals(fields.getFirst().getPermissions().getView())
                || !Set.of("admin").equals(fields.getFirst().getPermissions().getEdit())) throw CompleteIdentityPolicy.denied();
    }
    Map<String,String> newTicket(UserModel user, String client, int expiry) {
        policy.require(user,true); protectedMetadata();
        if (!"cleaning-suite-web".equals(client) || user.getEmail()==null || expiry<=Time.currentTime()) throw CompleteIdentityPolicy.denied();
        return Map.of("realm",realm.getId(),"user",user.getId(),"owner",user.getFirstAttribute(CompleteIdentityPolicy.OWNER),
                "email",user.getEmail(),"client",client,"expires",Integer.toString(expiry),"ticket",UUID.randomUUID().toString(),
                "epoch",Objects.toString(realm.getAttribute(EPOCH),""));
    }
    void installAfterDelivery(UserModel user, Map<String,String> data) {
        policy.require(user,true);
        user.setSingleAttribute(ATTRIBUTE,data.get("ticket"));
        // Do not remove the previous cache entry here: DB rollback must leave the
        // previous pointer/link valid. Successful pointer replacement supersedes it.
        session.singleUseObjects().put(key(realm,data.get("ticket")),Long.parseLong(data.get("expires"))-Time.currentTime(),data);
    }
    static void revoke(KeycloakSession session, RealmModel realm, UserModel user) {
        String ticket=user.getFirstAttribute(ATTRIBUTE);
        if (CompleteIdentityPolicy.canonicalUuid(ticket)) session.singleUseObjects().remove(key(realm,ticket));
        user.removeAttribute(ATTRIBUTE);
    }
    private boolean valid(UserModel user, String client, Map<String,String> data) {
        if (data==null || data.size()!=FIELDS.size() || FIELDS.stream().anyMatch(k -> data.get(k)==null)) return false;
        return policy.complete(() -> {
            if (!policy.authorized(user,true,true) || !realm.getId().equals(data.get("realm")) || !user.getId().equals(data.get("user"))
                    || !Objects.toString(realm.getAttribute(EPOCH),"").equals(data.get("epoch"))
                    || !Objects.equals(user.getEmail(),data.get("email")) || !Objects.equals(user.getFirstAttribute(CompleteIdentityPolicy.OWNER),data.get("owner"))
                    || !"cleaning-suite-web".equals(client) || !client.equals(data.get("client"))
                    || !CompleteIdentityPolicy.canonicalUuid(data.get("ticket")) || !data.get("ticket").equals(user.getFirstAttribute(ATTRIBUTE))) return false;
            try { return Long.parseLong(data.get("expires"))>Time.currentTime(); } catch (NumberFormatException e) { return false; }
        });
    }
    Map<String,String> fromToken(ExecuteActionsActionToken token, UserModel user, AuthenticationSessionModel auth) {
        protectedMetadata();
        String ticket=token.getNote("cleaning_ticket");
        if (!GuardedInvitationToken.TYPE.equals(token.getType()) || !CompleteIdentityPolicy.canonicalUuid(ticket)) throw CompleteIdentityPolicy.denied();
        Map<String,String> data=session.singleUseObjects().get(key(realm,ticket));
        if (!valid(user,auth.getClient().getClientId(),data) || !user.getId().equals(token.getUserId())
                || !data.get("owner").equals(token.getNote("cleaning_owner")) || !data.get("email").equals(token.getEmail())
                || !data.get("epoch").equals(token.getNote("cleaning_epoch"))
                || !data.get("client").equals(token.getIssuedFor()) || token.getExpiration()<=Time.currentTime()
                || !data.get("expires").equals(Long.toString(token.getExpiration()))) throw CompleteIdentityPolicy.denied();
        return data;
    }
    void attach(AuthenticationSessionModel auth, Map<String,String> data) {
        auth.setAuthNote(MARKER,"1");
        FIELDS.forEach(k -> auth.setAuthNote(PREFIX+k,data.get(k)));
    }
    Map<String,String> fromSession(UserModel user, AuthenticationSessionModel auth) {
        protectedMetadata();
        if (!"1".equals(auth.getAuthNote(MARKER))) throw CompleteIdentityPolicy.denied();
        Map<String,String> data=new HashMap<>(); FIELDS.forEach(k -> data.put(k,auth.getAuthNote(PREFIX+k)));
        if (!valid(user,auth.getClient().getClientId(),data) || !data.equals(session.singleUseObjects().get(key(realm,data.get("ticket"))))) throw CompleteIdentityPolicy.denied();
        return data;
    }
    void consume(UserModel user, AuthenticationSessionModel auth, Map<String,String> data) {
        // An earlier get or a queued replacement cannot determine the winner.
        Map<String,String> removed=session.singleUseObjects().remove(key(realm,data.get("ticket")));
        if (!data.equals(removed) || !valid(user,auth.getClient().getClientId(),data)) throw CompleteIdentityPolicy.denied();
        user.removeAttribute(ATTRIBUTE);
        // Never restore this cache ticket, even if credential storage/DB commit fails.
    }
    static String provenance(AuthenticationSessionModel auth) {
        String encoded=auth.getAuthNote(Constants.KEY);
        if (encoded==null) return null;
        try { return TokenVerifier.create(encoded,JsonWebToken.class).getToken().getType(); }
        catch (Exception e) { return "invalid-token"; }
    }
    void ordinaryChallenge(UserModel user, AuthenticationSessionModel auth) {
        policy.require(user,false);
        String provenance=provenance(auth);
        // CookieAuthenticator sets SSO_AUTH only after native identity-cookie and
        // reauthentication checks; KC_ACTION alone is caller-controlled, not proof.
        boolean authenticated=AuthenticatorUtil.isPasswordValidated(auth)
                || ("true".equals(auth.getAuthNote(AuthenticationManager.SSO_AUTH))
                    && UserModel.RequiredAction.UPDATE_PASSWORD.name().equals(auth.getClientNote(Constants.KC_ACTION)));
        if ((provenance!=null && !"reset-credentials".equals(provenance))
                || (provenance==null && !authenticated)) throw CompleteIdentityPolicy.denied();
        auth.setAuthNote(ORDINARY,"1");
        auth.setAuthNote(ORDINARY+".user",user.getId()); auth.setAuthNote(ORDINARY+".email",user.getEmail());
        auth.setAuthNote(ORDINARY+".owner",Objects.toString(user.getFirstAttribute(CompleteIdentityPolicy.OWNER),""));
        auth.setAuthNote(ORDINARY+".client",auth.getClient().getClientId());
    }
    void ordinarySubmission(UserModel user, AuthenticationSessionModel auth) {
        policy.require(user,false);
        if (!"1".equals(auth.getAuthNote(ORDINARY)) || !user.getId().equals(auth.getAuthNote(ORDINARY+".user"))
                || !Objects.equals(user.getEmail(),auth.getAuthNote(ORDINARY+".email"))
                || !Objects.toString(user.getFirstAttribute(CompleteIdentityPolicy.OWNER),"").equals(auth.getAuthNote(ORDINARY+".owner"))
                || !auth.getClient().getClientId().equals(auth.getAuthNote(ORDINARY+".client"))) throw CompleteIdentityPolicy.denied();
        String provenance=provenance(auth);
        if (provenance!=null && !"reset-credentials".equals(provenance)) throw CompleteIdentityPolicy.denied();
    }
}
