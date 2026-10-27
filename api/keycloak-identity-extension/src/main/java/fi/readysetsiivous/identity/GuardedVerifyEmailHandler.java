package fi.readysetsiivous.identity;

import jakarta.ws.rs.core.Response;
import org.keycloak.authentication.actiontoken.ActionTokenContext;
import org.keycloak.authentication.actiontoken.verifyemail.VerifyEmailActionToken;
import org.keycloak.authentication.actiontoken.verifyemail.VerifyEmailActionTokenHandler;
import org.keycloak.models.UserModel;

/** Native VerifyEmail.processAction resends mail; this handler performs the actual write. */
public final class GuardedVerifyEmailHandler extends VerifyEmailActionTokenHandler {
    @Override public int order() { return 100; }
    @Override public Response handleToken(VerifyEmailActionToken token, ActionTokenContext<VerifyEmailActionToken> context) {
        if (!CompleteIdentityPolicy.enabled(context.getRealm())) return super.handleToken(token,context);
        try {
            var auth=context.getAuthenticationSession(); var user=auth.getAuthenticatedUser();
            new CompleteIdentityPolicy(context.getSession(),context.getRealm()).require(user,false);
            if (auth.getAuthNote(InvitationTickets.MARKER)!=null)
                new InvitationTickets(context.getSession(),context.getRealm()).fromSession(user,auth);
            else if (user.getFirstAttribute(CompleteIdentityPolicy.OWNER)!=null && user.getRequiredActionsStream().anyMatch(UserModel.RequiredAction.UPDATE_PASSWORD.name()::equals))
                throw CompleteIdentityPolicy.denied();
            return super.handleToken(token,context);
        } catch (jakarta.ws.rs.ForbiddenException e) { return GuardedLegacyActionsHandler.rejected(context); }
    }
}
