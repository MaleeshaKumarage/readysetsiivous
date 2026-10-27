package fi.readysetsiivous.identity;

import jakarta.ws.rs.Path;
import jakarta.ws.rs.ext.Provider;
import org.keycloak.models.KeycloakSession;

@Provider
public final class IdentityRoot {
    private final KeycloakSession session;
    IdentityRoot(KeycloakSession session) { this.session = session; }
    @Path("v1") public IdentityResource versionOne() { return new IdentityResource(session); }
}
