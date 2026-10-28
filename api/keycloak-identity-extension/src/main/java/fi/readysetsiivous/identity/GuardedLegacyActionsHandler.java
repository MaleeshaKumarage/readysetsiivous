package fi.readysetsiivous.identity;

import jakarta.ws.rs.core.Response;
import org.keycloak.authentication.actiontoken.ActionTokenContext;
import org.keycloak.authentication.actiontoken.execactions.ExecuteActionsActionToken;
import org.keycloak.authentication.actiontoken.execactions.ExecuteActionsActionTokenHandler;
import org.keycloak.events.Errors;
import org.keycloak.forms.login.LoginFormsProvider;

/** Same provider ID, verified higher-order selection; other realms remain native. */
public final class GuardedLegacyActionsHandler extends ExecuteActionsActionTokenHandler {
    @Override public int order() { return 100; }
    @Override public Response handleToken(ExecuteActionsActionToken token, ActionTokenContext<ExecuteActionsActionToken> context) {
        return CompleteIdentityPolicy.enabled(context.getRealm()) ? rejected(context) : super.handleToken(token,context);
    }
    static Response rejected(ActionTokenContext<?> context) {
        context.getEvent().error(Errors.NOT_ALLOWED);
        return context.getSession().getProvider(LoginFormsProvider.class).setAuthenticationSession(context.getAuthenticationSession())
                .setError("This setup link is no longer valid. Request a new invitation.").createErrorPage(Response.Status.BAD_REQUEST);
    }
}
