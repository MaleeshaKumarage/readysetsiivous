package fi.readysetsiivous.tests;

import org.keycloak.Config;
import org.keycloak.events.Event;
import org.keycloak.events.EventListenerProvider;
import org.keycloak.events.EventListenerProviderFactory;
import org.keycloak.events.EventType;
import org.keycloak.events.admin.AdminEvent;
import org.keycloak.models.AbstractKeycloakTransaction;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.KeycloakSessionFactory;
import org.keycloak.models.ModelException;
import org.keycloak.models.RealmModel;

/** TEST IMAGE ONLY. Never copied into the normal extension artifact or image. */
public final class PasswordRollbackListenerFactory implements EventListenerProviderFactory {
    public static final String ARM = "synthetic_test_rollback_password_user";
    private static final String ENLISTED = "synthetic.test.password.rollback.enlisted";
    @Override public String getId() { return "synthetic-password-rollback-test"; }
    @Override public boolean isGlobal() { return true; }
    @Override public int order() { return 1000; }
    @Override public EventListenerProvider create(KeycloakSession session) {
        return new EventListenerProvider() {
            public void onEvent(Event event) {
                if (event.getType() != EventType.UPDATE_PASSWORD || event.getError() != null) return;
                RealmModel realm = session.realms().getRealm(event.getRealmId());
                if (realm == null || !realm.getName().startsWith("guarded-invite-")
                        || event.getUserId() == null || !event.getUserId().equals(realm.getAttribute(ARM))
                        || Boolean.TRUE.equals(session.getAttribute(ENLISTED))) return;
                session.setAttribute(ENLISTED, Boolean.TRUE);
                // Pinned UpdatePassword.processAction calls updateCredential and
                // context.success before deprecatedEvent.success (UPDATE_PASSWORD).
                // EventBuilder invokes this success callback inside that request's
                // transaction. Force failure at prepare, after real storage code
                // ran but before the credential transaction can commit.
                // https://github.com/keycloak/keycloak/blob/26.8.1/services/src/main/java/org/keycloak/authentication/requiredactions/UpdatePassword.java
                session.getTransactionManager().enlistPrepare(new AbstractKeycloakTransaction() {
                    @Override protected void commitImpl() {
                        throw new ModelException("Synthetic credential rollback requested");
                    }
                    @Override protected void rollbackImpl() {}
                });
            }
            public void onEvent(AdminEvent event, boolean includeRepresentation) {}
            public void close() {}
        };
    }
    @Override public void init(Config.Scope config) {}
    @Override public void postInit(KeycloakSessionFactory factory) {}
    @Override public void close() {}
}
