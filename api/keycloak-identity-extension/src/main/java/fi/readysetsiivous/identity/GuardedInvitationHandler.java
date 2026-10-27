package fi.readysetsiivous.identity;

import jakarta.ws.rs.core.Response;
import org.keycloak.authentication.actiontoken.ActionTokenContext;
import org.keycloak.authentication.actiontoken.execactions.ExecuteActionsActionToken;
import org.keycloak.authentication.actiontoken.execactions.ExecuteActionsActionTokenHandler;
import org.keycloak.models.UserModel;

/** Guard before the native handler can set emailVerified or enqueue password actions. */
public final class GuardedInvitationHandler extends ExecuteActionsActionTokenHandler {
    @Override public String getId() { return GuardedInvitationToken.TYPE; }
    @SuppressWarnings({"unchecked","rawtypes"})
    @Override public Class<ExecuteActionsActionToken> getTokenClass() { return (Class)GuardedInvitationToken.class; }
    @Override public Response handleToken(ExecuteActionsActionToken token, ActionTokenContext<ExecuteActionsActionToken> context) {
        var auth=context.getAuthenticationSession(); UserModel user=auth.getAuthenticatedUser();
        try {
            InvitationTickets tickets=new InvitationTickets(context.getSession(),context.getRealm());
            var data=tickets.fromToken(token,user,auth);
            tickets.attach(auth,data);
            if (!context.isAuthenticationSessionFresh() && !token.getRequiredActions().contains(UserModel.RequiredAction.UPDATE_PASSWORD.name()))
                tickets.consume(user,auth,data);
            return super.handleToken(token,context);
        } catch (jakarta.ws.rs.ForbiddenException e) { return GuardedLegacyActionsHandler.rejected(context); }
    }
    @Override public boolean canUseTokenRepeatedly(ExecuteActionsActionToken token, ActionTokenContext<ExecuteActionsActionToken> context) { return true; }
}
