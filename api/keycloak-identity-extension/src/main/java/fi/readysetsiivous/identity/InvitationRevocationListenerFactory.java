package fi.readysetsiivous.identity;

import com.fasterxml.jackson.databind.JsonNode;
import java.util.*;
import org.keycloak.Config;
import org.keycloak.authorization.fgap.AdminPermissionsSchema;
import org.keycloak.events.Event;
import org.keycloak.events.EventListenerProvider;
import org.keycloak.events.EventListenerProviderFactory;
import org.keycloak.events.admin.AdminEvent;
import org.keycloak.events.admin.OperationType;
import org.keycloak.models.*;
import org.keycloak.util.JsonSerialization;

/**
 * Native administrator writes must not revive a previously issued invitation.
 * Keycloak 26.8.1 AdminEventBuilder invokes global listeners before commit even
 * with admin-event storage disabled. It catches listener errors, so a throwing
 * prepare transaction (not only setRollbackOnly) is essential for a true veto.
 */
public final class InvitationRevocationListenerFactory implements EventListenerProviderFactory {
    private static final String STATE="cleaning.identity.prepare-guard";
    @Override public String getId() { return "cleaning-invitation-revocation"; }
    @Override public boolean isGlobal() { return true; }
    @Override public int order() { return 100; }
    @Override public EventListenerProvider create(KeycloakSession session) {
        return new EventListenerProvider() {
            public void onEvent(Event event) {} // Browser credential writes use guarded required actions.
            public void onEvent(AdminEvent event, boolean includeRepresentation) { administrativeWrite(session,event); }
            public void close() {}
        };
    }
    @Override public void init(Config.Scope config) {}
    @Override public void postInit(KeycloakSessionFactory factory) {}
    @Override public void close() {}

    private static final class PrepareGuard extends AbstractKeycloakTransaction {
        final KeycloakSession session;
        final Set<String> inventories=new HashSet<>();
        boolean failed;
        PrepareGuard(KeycloakSession session) { this.session=session; }
        @Override protected void commitImpl() {
            if (failed) throw new ModelException("Identity revocation failed; request a new operation");
            for (String realmId : inventories) {
                RealmModel realm=session.realms().getRealm(realmId);
                if (realm==null || !CompleteIdentityPolicy.enabled(realm)) continue;
                boolean unsafe=AdminPermissionsSchema.runWithoutAuthorization(session,() -> {
                    ClientModel management=realm.getClientByClientId("realm-management");
                    if (management==null) return true;
                    // Strong edge invariant: no realm-management role may have
                    // an inbound grant outside its own builtin client-role graph.
                    // Checking even unassigned carriers prevents write-skew between
                    // assigning a benign role and making that role privileged.
                    if (bounded(realm.getRolesStream().limit(10001).toList(),10000).stream()
                            .anyMatch(r -> unsafeComposite(r,management))) return true;
                    for (ClientModel client : bounded(realm.getClientsStream().limit(1001).toList(),1000)) {
                        if (client.getId().equals(management.getId())) continue;
                        if (bounded(client.getRolesStream().limit(10001).toList(),10000).stream()
                                .anyMatch(r -> unsafeComposite(r,management))) return true;
                    }
                    ArrayDeque<GroupModel> remaining=new ArrayDeque<>(bounded(realm.getTopLevelGroupsStream().limit(10001).toList(),10000));
                    Set<String> seen=new HashSet<>();
                    while (!remaining.isEmpty()) {
                        GroupModel group=remaining.removeFirst();
                        if (!seen.add(group.getId()) || seen.size()>10000) return true;
                        if (bounded(group.getRoleMappingsStream().limit(10001).toList(),10000).stream()
                                .anyMatch(r -> managementRole(r,management))) return true;
                        List<GroupModel> children=bounded(group.getSubGroupsStream().limit(10001).toList(),10000);
                        if (remaining.size()+seen.size()+children.size()>10000) return true;
                        remaining.addAll(children);
                    }
                    List<UserModel> users=session.users().searchForUserStream(realm,
                            Map.of(UserModel.INCLUDE_SERVICE_ACCOUNT,"true"),0,10001).limit(10001).toList();
                    if (users.size()>10000) return true; // Incomplete inventory cannot authorize a grant.
                    return users.stream().anyMatch(u -> bounded(u.getRoleMappingsStream().limit(10001).toList(),10000)
                            .stream().anyMatch(r -> managementRole(r,management)));
                });
                if (unsafe) throw new ModelException("Use a separate master-realm infrastructure administrator");
            }
        }
        @Override protected void rollbackImpl() {} // Never restore immediately consumed/revoked cache tickets.
    }
    private static <T> List<T> bounded(List<T> values, int maximum) {
        if (values.size()>maximum) throw new ModelException("Identity security inventory exceeds the verified bound");
        return values;
    }
    private static boolean managementRole(RoleModel role, ClientModel management) {
        return role.isClientRole() && management.getId().equals(role.getContainerId());
    }
    private static boolean unsafeComposite(RoleModel role, ClientModel management) {
        return bounded(role.getCompositesStream().limit(10001).toList(),10000).stream().anyMatch(r -> managementRole(r,management));
    }
    private static PrepareGuard guard(KeycloakSession session) {
        Object existing=session.getAttribute(STATE);
        if (existing instanceof PrepareGuard guard) return guard;
        PrepareGuard guard=new PrepareGuard(session);
        session.getTransactionManager().enlistPrepare(guard);
        session.setAttribute(STATE,guard);
        return guard;
    }
    private static JsonNode realmBody(AdminEvent event) {
        if (event.getRepresentation()==null) return null;
        try {
            return JsonSerialization.readValue(event.getRepresentation(),JsonNode.class);
        } catch (Exception e) { throw new ModelException("Realm security change could not be checked"); }
    }
    private static void administrativeWrite(KeycloakSession session, AdminEvent event) {
        if (event.getError()!=null || event.getRealmId()==null) return;
        // The caller may be authenticated in master while modifying another realm.
        RealmModel realm=session.realms().getRealm(event.getRealmId());
        if (!CompleteIdentityPolicy.enabled(realm)) return;
        String path=Objects.toString(event.getResourcePath(),"");
        while (path.startsWith("/")) path=path.substring(1);
        if (path.startsWith("cleaning-identity/")) return; // Explicit API hooks already revoke; issuance must survive its audit.
        // JAX-RS permits matrix parameters and empty separator segments; classify
        // the same native route rather than allowing URI spelling to bypass hooks.
        String[] segments=Arrays.stream(path.split("/")).map(s -> s.split(";",2)[0])
                .filter(s -> !s.isEmpty()).toArray(String[]::new);
        boolean users=segments.length>=1 && segments[0].equals("users");
        boolean profile=users && segments.length==2 && segments[1].equals("profile");
        boolean graph=segments.length>=1 && Set.of("groups","roles","roles-by-id","clients","default-groups","partialImport","components").contains(segments[0]);
        boolean roleMappings=users && Arrays.asList(segments).contains("role-mappings");
        PrepareGuard prepare=guard(session);
        try {
            boolean realmPath=segments.length==0 || (segments.length==1 && segments[0].equals("realm"));
            JsonNode body=realmPath ? realmBody(event) : null;
            boolean realmChange=body!=null && List.of("defaultRoles","defaultGroups","defaultRole","roles","groups","clients","users").stream().anyMatch(body::has);
            boolean realmEpoch=body!=null && (body.has("attributes") || body.has("enabled") || body.has("adminPermissionsEnabled"));
            if (realmPath && realm.isAdminPermissionsEnabled()) {
                prepare.failed=true;
                throw new ModelException("Employee identity realms must keep fine-grained Admin REST grants disabled");
            }
            if (!users && !graph && !realmChange && !realmEpoch && realm.isEnabled()) return;
            AdminPermissionsSchema.runWithoutAuthorization(session,() -> {
                UserModel target=null;
                if (users && segments.length>=2 && CompleteIdentityPolicy.canonicalUuid(segments[1]))
                    target=session.users().getUserById(realm,segments[1]);
                if (target!=null) InvitationTickets.revoke(session,realm,target);
                if (graph || profile || realmChange || realmEpoch || !realm.isEnabled())
                    realm.setAttribute(InvitationTickets.EPOCH,UUID.randomUUID().toString());
                boolean cleanup=event.getOperationType()==OperationType.DELETE
                        || (users && target!=null && !target.isEnabled() && event.getOperationType()==OperationType.UPDATE);
                if (!cleanup && (graph || realmChange || roleMappings ||
                        (users && (event.getOperationType()==OperationType.CREATE || event.getOperationType()==OperationType.UPDATE))))
                    prepare.inventories.add(realm.getId());
            });
        } catch (RuntimeException e) {
            prepare.failed=true;
            // AdminEventBuilder swallows callback exceptions. The enlisted prepare
            // guard still throws before JPA commit, including explicit native commits.
            throw new ModelException("Identity security change could not be checked");
        }
    }
}
