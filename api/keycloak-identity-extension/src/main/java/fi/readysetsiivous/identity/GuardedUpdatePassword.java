package fi.readysetsiivous.identity;

import org.keycloak.authentication.RequiredActionContext;
import org.keycloak.authentication.RequiredActionProvider;
import org.keycloak.authentication.requiredactions.UpdatePassword;
import org.keycloak.models.*;
import org.keycloak.policy.PasswordPolicyManagerProvider;
import org.keycloak.policy.PolicyError;
import org.keycloak.services.messages.Messages;
import org.keycloak.services.validation.Validation;

/** Guards both open forms and the actual native credential storage. */
public final class GuardedUpdatePassword extends UpdatePassword {
    @Override public int order() { return 100; }
    @Override public RequiredActionProvider create(KeycloakSession session) {
        return CompleteIdentityPolicy.enabled(session.getContext().getRealm()) ? new GuardedUpdatePassword() : new UpdatePassword();
    }
    private static void reject(RequiredActionContext context) { context.failure("This setup session is no longer valid. Start again or request a new invitation."); }
    @Override public void requiredActionChallenge(RequiredActionContext context) {
        if (!CompleteIdentityPolicy.enabled(context.getRealm())) { super.requiredActionChallenge(context); return; }
        try {
            InvitationTickets tickets=new InvitationTickets(context.getSession(),context.getRealm());
            if (context.getAuthenticationSession().getAuthNote(InvitationTickets.MARKER)!=null)
                tickets.fromSession(context.getUser(),context.getAuthenticationSession());
            else tickets.ordinaryChallenge(context.getUser(),context.getAuthenticationSession());
            super.requiredActionChallenge(context);
        } catch (jakarta.ws.rs.ForbiddenException e) { reject(context); }
    }
    private boolean prevalidate(RequiredActionContext context) {
        var data=context.getHttpRequest().getDecodedFormParameters();
        String password=data.getFirst("password-new"), confirm=data.getFirst("password-confirm");
        String message=null; Object[] parameters=new Object[0];
        if (Validation.isBlank(password)) message=Messages.MISSING_PASSWORD;
        else if (!password.equals(confirm)) message=Messages.NOTMATCH_PASSWORD;
        else {
            PolicyError error=context.getSession().getProvider(PasswordPolicyManagerProvider.class).validate(context.getRealm(),context.getUser(),password);
            if (error!=null) { message=error.getMessage(); parameters=error.getParameters(); }
        }
        if (message==null) return true;
        context.challenge(context.form().setAttribute("username",context.getUser().getUsername()).setError(message,parameters)
                .createResponse(UserModel.RequiredAction.UPDATE_PASSWORD));
        return false;
    }
    @Override public void processAction(RequiredActionContext context) {
        if (!CompleteIdentityPolicy.enabled(context.getRealm())) { super.processAction(context); return; }
        try {
            InvitationTickets tickets=new InvitationTickets(context.getSession(),context.getRealm());
            var auth=context.getAuthenticationSession();
            if (auth.getAuthNote(InvitationTickets.MARKER)!=null) {
                var data=tickets.fromSession(context.getUser(),auth);
                if (!prevalidate(context)) return;
                tickets.consume(context.getUser(),auth,data);
            } else {
                tickets.ordinarySubmission(context.getUser(),auth);
                if (!prevalidate(context)) return;
            }
            // Never call native storage on an invalid/revoked ticket, or a path
            // where native validation might succeed after our precheck failed.
            super.processAction(context);
            if (context.getStatus()==RequiredActionContext.Status.SUCCESS)
                InvitationTickets.revoke(context.getSession(),context.getRealm(),context.getUser());
            if (auth.getAuthNote(InvitationTickets.MARKER)!=null && context.getStatus()!=RequiredActionContext.Status.SUCCESS)
                reject(context); // consumed tickets are never offered as retryable storage failures
        } catch (jakarta.ws.rs.ForbiddenException e) { reject(context); }
    }
}
