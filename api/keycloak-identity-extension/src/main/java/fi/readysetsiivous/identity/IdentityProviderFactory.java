package fi.readysetsiivous.identity;

import org.keycloak.Config;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.KeycloakSessionFactory;
import org.keycloak.services.resource.RealmResourceProvider;
import org.keycloak.services.resource.RealmResourceProviderFactory;

/** Version-pinned custom REST provider; no administrative REST delegation. */
public final class IdentityProviderFactory implements RealmResourceProviderFactory {
    public String getId() { return "cleaning-identity"; }
    public RealmResourceProvider create(KeycloakSession session) {
        return new RealmResourceProvider() {
            public Object getResource() { return new IdentityRoot(session); }
            public void close() {}
        };
    }
    public void init(Config.Scope config) {}
    public void postInit(KeycloakSessionFactory factory) {}
    public void close() {}
}
