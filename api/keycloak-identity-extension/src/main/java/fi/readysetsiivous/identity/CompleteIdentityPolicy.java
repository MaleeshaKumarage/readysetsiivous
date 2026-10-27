package fi.readysetsiivous.identity;

import jakarta.ws.rs.ForbiddenException;
import java.util.*;
import java.util.function.Supplier;
import org.keycloak.authorization.fgap.AdminPermissionsSchema;
import org.keycloak.models.*;

/** Complete server-model policy shared by API and browser callbacks. */
final class CompleteIdentityPolicy {
    static final String OWNER = "cleaning_employee_id";
    static final String RUNTIME = "cleaning_identity_runtime_client_id";
    static final Map<String,String> GROUP_ROLES = Map.of("managed-users", "", "app-employee", "employee", "app-manager", "manager", "app-admin", "admin");
    final KeycloakSession session;
    final RealmModel realm;
    final ClientModel management;
    final Map<String,GroupModel> groups;

    static boolean enabled(RealmModel realm) {
        return realm != null && !"master".equals(realm.getName()) && realm.getAttribute(RUNTIME) != null;
    }
    static boolean canonicalUuid(String value) {
        try { return value != null && UUID.fromString(value).toString().equals(value) && !new UUID(0,0).toString().equals(value); }
        catch (IllegalArgumentException e) { return false; }
    }
    CompleteIdentityPolicy(KeycloakSession session, RealmModel realm) {
        this.session = session; this.realm = realm;
        if (!enabled(realm) || !realm.isEnabled() || realm.isAdminPermissionsEnabled() || !canonicalUuid(realm.getAttribute(RUNTIME))) throw denied();
        this.management = realm.getClientByClientId("realm-management");
        if (management == null) throw denied();
        this.groups = complete(this::validatedGroups);
    }
    <T> T complete(Supplier<T> work) { return AdminPermissionsSchema.runWithoutAuthorization(session,work); }
    static ForbiddenException denied() { return new ForbiddenException("Identity operation is not permitted"); }
    boolean administrative(UserModel user) { return management.getRolesStream().anyMatch(user::hasRole); }
    private Map<String,GroupModel> validatedGroups() {
        Map<String,GroupModel> found = new HashMap<>();
        List<GroupModel> roots = realm.getTopLevelGroupsStream().limit(10001).toList();
        if (roots.size() > 10000) throw denied();
        for (var desired : GROUP_ROLES.entrySet()) {
            List<GroupModel> matches = roots.stream().filter(g -> desired.getKey().equals(g.getName())).toList();
            if (matches.size() != 1) throw denied();
            GroupModel group = matches.getFirst();
            if (group.getParentId() != null || group.getSubGroupsStream().findAny().isPresent()
                    || group.getRoleMappingsStream().anyMatch(r -> r.isClientRole() || r.isComposite()
                       || desired.getValue().isEmpty() || !desired.getValue().equals(r.getName()))) throw denied();
            List<RoleModel> mappings = group.getRealmRoleMappingsStream().toList();
            if (mappings.size() != (desired.getValue().isEmpty() ? 0 : 1)) throw denied();
            if (!desired.getValue().isEmpty()) {
                RoleModel role = realm.getRole(desired.getValue());
                if (role == null || role.isComposite() || !role.getId().equals(mappings.getFirst().getId())) throw denied();
            }
            found.put(desired.getKey(),group);
        }
        return Map.copyOf(found);
    }
    boolean authorized(UserModel user, boolean ownerRequired, boolean enabledRequired) {
        if (user == null || (enabledRequired && !user.isEnabled()) || user.getServiceAccountClientLink() != null || administrative(user)) return false;
        List<GroupModel> memberships = user.getGroupsStream().limit(102).toList();
        if (memberships.size() > 100 || memberships.stream().noneMatch(g -> g.getId().equals(groups.get("managed-users").getId()))
                || memberships.stream().anyMatch(g -> groups.values().stream().noneMatch(a -> a.getId().equals(g.getId())))) return false;
        List<String> owner = user.getAttributeStream(OWNER).toList();
        if (owner.isEmpty()) return !ownerRequired;
        return owner.size() == 1 && canonicalUuid(owner.getFirst());
    }
    void require(UserModel user, boolean ownerRequired) {
        if (!complete(() -> authorized(user,ownerRequired,true))) throw denied();
    }
}
