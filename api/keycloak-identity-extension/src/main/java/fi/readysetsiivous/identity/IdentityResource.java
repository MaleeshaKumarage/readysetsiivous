package fi.readysetsiivous.identity;

import com.fasterxml.jackson.databind.JsonNode;
import jakarta.ws.rs.*;
import jakarta.ws.rs.core.MediaType;
import jakarta.ws.rs.core.Response;
import jakarta.ws.rs.ext.Provider;
import java.net.URI;
import java.util.*;
import java.util.concurrent.TimeUnit;
import java.util.function.Supplier;
import org.keycloak.authentication.actiontoken.execactions.ExecuteActionsActionToken;
import org.keycloak.authorization.fgap.AdminPermissionsSchema;
import org.keycloak.common.util.Time;
import org.keycloak.email.EmailException;
import org.keycloak.email.EmailTemplateProvider;
import org.keycloak.models.*;
import org.keycloak.events.admin.OperationType;
import org.keycloak.events.admin.ResourceType;
import org.keycloak.services.managers.AppAuthManager;
import org.keycloak.services.managers.AuthenticationManager;
import org.keycloak.services.resources.LoginActionsService;
import org.keycloak.services.resources.admin.UserResource;
import org.keycloak.services.resources.admin.AdminAuth;
import org.keycloak.services.resources.admin.AdminEventBuilder;
import org.keycloak.userprofile.*;

/**
 * Explicit employee API. Model reads must not inherit FGAP visibility filtering.
 * Sources: Keycloak 26.8.1 AppAuthManager, AdminPermissionsSchema, UserResource.
 * This is a development boundary; operator promotion races remain a rollout gate.
 */
@Provider
@Produces(MediaType.APPLICATION_JSON)
@Consumes(MediaType.APPLICATION_JSON)
public final class IdentityResource {
    private static final String OWNER = "cleaning_employee_id";
    private static final Set<String> ACTIONS = Set.of("VERIFY_EMAIL", "UPDATE_PASSWORD");
    private static final Map<String,String> GROUP_ROLES = Map.of("managed-users", "", "app-employee", "employee", "app-manager", "manager", "app-admin", "admin");
    private final KeycloakSession session;
    private final RealmModel realm;
    private final Map<String,GroupModel> groups;
    private final ClientModel management;
    private final AuthenticationManager.AuthResult principal;

    IdentityResource(KeycloakSession session) {
        this.session = session;
        this.realm = session.getContext().getRealm();
        if (realm == null || !realm.isEnabled() || realm.getName().equals("master") || realm.isAdminPermissionsEnabled())
            throw forbidden();
        String clientId = realm.getAttribute("cleaning_identity_runtime_client_id");
        if (!canonicalUuid(clientId)) throw forbidden();
        ClientModel expected = realm.getClientById(clientId);
        if (expected == null || !expected.isEnabled() || expected.isPublicClient() || !expected.isServiceAccountsEnabled()
                || expected.isStandardFlowEnabled() || expected.isDirectAccessGrantsEnabled() || expected.isFullScopeAllowed()) throw forbidden();
        var auth = new AppAuthManager.BearerTokenAuthenticator(session).setRealm(realm).setAudience("cleaning-identity-runtime").authenticate();
        if (auth == null || auth.user() == null || auth.client() == null || !expected.getId().equals(auth.client().getId())
                || !expected.getClientId().equals(auth.token().getIssuedFor())
                || !expected.getId().equals(auth.user().getServiceAccountClientLink())) throw forbidden();
        this.principal = auth;
        UserModel service = complete(() -> session.users().getServiceAccount(expected));
        if (service == null || !service.getId().equals(auth.user().getId())) throw forbidden();
        this.management = realm.getClientByClientId("realm-management");
        if (management == null || complete(() -> administrative(service) || service.getGroupsStream().findAny().isPresent())) throw forbidden();
        this.groups = complete(this::validatedGroups);
    }

    private <T> T complete(Supplier<T> work) { return AdminPermissionsSchema.runWithoutAuthorization(session, work); }
    private void completeWrite(Runnable work) { AdminPermissionsSchema.runWithoutAuthorization(session, work); }
    private static ForbiddenException forbidden() { return new ForbiddenException("Identity operation is not permitted"); }
    private static BadRequestException bad() { return new BadRequestException("Invalid identity request"); }
    private static boolean canonicalUuid(String value) {
        try { return value != null && UUID.fromString(value).toString().equals(value) && !new UUID(0,0).toString().equals(value); }
        catch (IllegalArgumentException e) { return false; }
    }
    private boolean administrative(UserModel user) { return management.getRolesStream().anyMatch(user::hasRole); }
    private Map<String,GroupModel> validatedGroups() {
        Map<String,GroupModel> found = new HashMap<>();
        List<GroupModel> roots = realm.getTopLevelGroupsStream().limit(10001).toList();
        if (roots.size() > 10000) throw forbidden();
        for (var desired : GROUP_ROLES.entrySet()) {
            List<GroupModel> matches = roots.stream().filter(g -> desired.getKey().equals(g.getName())).toList();
            if (matches.size() != 1) throw forbidden();
            GroupModel group = matches.getFirst();
            if (group.getParentId() != null || group.getSubGroupsStream().findAny().isPresent()
                    || group.getRoleMappingsStream().anyMatch(r -> r.isClientRole() || r.isComposite()
                       || desired.getValue().isEmpty() || !desired.getValue().equals(r.getName()))) throw forbidden();
            List<RoleModel> roleMappings = group.getRealmRoleMappingsStream().toList();
            if (roleMappings.size() != (desired.getValue().isEmpty() ? 0 : 1)) throw forbidden();
            if (!desired.getValue().isEmpty()) {
                RoleModel role = realm.getRole(desired.getValue());
                if (role == null || role.isComposite() || !role.getId().equals(roleMappings.getFirst().getId())) throw forbidden();
            }
            found.put(desired.getKey(), group);
        }
        return found;
    }
    private boolean authorized(UserModel user, boolean ownerRequired) {
        if (user == null || user.getServiceAccountClientLink() != null || administrative(user)) return false;
        List<GroupModel> memberships = user.getGroupsStream().limit(102).toList();
        if (memberships.size() > 100 || memberships.stream().noneMatch(g -> g.getId().equals(groups.get("managed-users").getId()))
                || memberships.stream().anyMatch(g -> groups.values().stream().noneMatch(allowed -> allowed.getId().equals(g.getId())))) return false;
        List<String> owner = user.getAttributeStream(OWNER).toList();
        if (owner.isEmpty()) return !ownerRequired;
        return owner.size() == 1 && canonicalUuid(owner.getFirst());
    }
    private UserModel target(String id, boolean mutation) {
        UserModel user = session.users().getUserById(realm, id);
        if (user == null) throw new NotFoundException("Identity not found");
        if (!authorized(user, mutation)) throw forbidden();
        return user;
    }
    private GroupModel group(String id) {
        return groups.values().stream().filter(g -> g.getId().equals(id)).findFirst().orElseThrow(IdentityResource::forbidden);
    }
    private Map<String,Object> groupDto(GroupModel group) { return Map.of("id", group.getId(), "name", group.getName(), "path", "/" + group.getName()); }
    private Map<String,Object> userDto(UserModel user) {
        Map<String,Object> result = new LinkedHashMap<>();
        result.put("id", user.getId()); result.put("username", user.getUsername()); result.put("email", user.getEmail());
        result.put("firstName", user.getFirstName()); result.put("lastName", user.getLastName());
        result.put("enabled", user.isEnabled()); result.put("emailVerified", user.isEmailVerified());
        result.put("requiredActions", user.getRequiredActionsStream().toList());
        List<String> owner = user.getAttributeStream(OWNER).toList();
        result.put("attributes", owner.isEmpty() ? Map.of() : Map.of(OWNER, owner));
        return result;
    }
    private Map<String,Object> roleDto(RoleModel role) { return Map.of("id", role.getId(), "name", role.getName(), "composite", role.isComposite(), "clientRole", role.isClientRole()); }
    private void audit(OperationType operation, String userId) {
        // Native audit sink, with caller/target metadata only; no credentials or tokens.
        new AdminEventBuilder(realm,new AdminAuth(realm,principal.token(),principal.user(),principal.client()),session,session.getContext().getConnection())
                .resource(ResourceType.USER).operation(operation).resourcePath("cleaning-identity", "v1", "users", userId).success();
    }
    private void fields(JsonNode body, Set<String> allowed) {
        if (body == null || !body.isObject()) throw bad();
        body.fieldNames().forEachRemaining(name -> { if (!allowed.contains(name)) throw forbidden(); });
    }
    private String text(JsonNode body, String field, boolean required) {
        JsonNode value = body.get(field);
        if (value == null && !required) return null;
        if (value == null || !value.isTextual() || value.asText().length() > 320 || (required && value.asText().isBlank())) throw bad();
        return value.asText();
    }
    private String owner(JsonNode body) {
        JsonNode attributes = body.get("attributes");
        if (attributes == null || !attributes.isObject() || attributes.size() != 1 || !attributes.has(OWNER)) throw forbidden();
        JsonNode value = attributes.get(OWNER);
        if (!value.isArray() || value.size() != 1 || !value.get(0).isTextual() || !canonicalUuid(value.get(0).asText())) throw forbidden();
        return value.get(0).asText();
    }
    private List<UserModel> visibleMembers(GroupModel group) {
        List<UserModel> candidates = session.users().getGroupMembersStream(realm, group).limit(10001).toList();
        if (candidates.size() > 10000) throw new ServiceUnavailableException("Identity directory exceeds the verified bound");
        return candidates.stream().filter(u -> authorized(u, false)).toList();
    }
    private static <T> List<T> page(List<T> source, int first, int max) {
        if (first < 0 || max < 1 || max > 1000) throw bad();
        return source.stream().skip(first).limit(max).toList();
    }

    @GET public Map<String,Object> realm() {
        Map<String,String> attrs = new HashMap<>();
        String frontend = realm.getAttribute("frontendUrl");
        if (frontend != null) attrs.put("frontendUrl", frontend);
        Map<String,String> smtp = new HashMap<>();
        for (String key : List.of("host", "from")) { String value = realm.getSmtpConfig().get(key); if (value != null) smtp.put(key,value); }
        return Map.of("realm", realm.getName(), "enabled", realm.isEnabled(), "attributes", attrs, "smtpServer", smtp);
    }
    @GET @Path("roles/{name}") public Object role(@PathParam("name") String name) {
        if (!Set.of("employee", "manager", "admin").contains(name)) throw forbidden();
        return complete(() -> roleDto(realm.getRole(name)));
    }
    @GET @Path("group-by-path/{name}") public Object fixedGroup(@PathParam("name") String name) {
        if (!groups.containsKey(name)) throw forbidden();
        return groupDto(groups.get(name));
    }
    @GET @Path("groups/{id}/role-mappings") public Object groupMappings(@PathParam("id") String id) {
        return complete(() -> Map.of("realmMappings", group(id).getRealmRoleMappingsStream().map(this::roleDto).toList(), "clientMappings", Map.of()));
    }
    @GET @Path("groups/{id}/role-mappings/realm/composite") public Object effectiveGroupRoles(@PathParam("id") String id) {
        return complete(() -> group(id).getRealmRoleMappingsStream().map(this::roleDto).toList());
    }
    @GET @Path("groups/{id}/members") public Object members(@PathParam("id") String id, @QueryParam("first") @DefaultValue("0") int first, @QueryParam("max") @DefaultValue("100") int max) {
        return complete(() -> page(visibleMembers(group(id)), first, max).stream().map(this::userDto).toList());
    }
    @GET @Path("users/profile") public Object profile() {
        // Only the one setup-owned field needed for preflight; no profile write API.
        var config = session.getProvider(UserProfileProvider.class).getConfiguration();
        return Map.of("attributes", config.getAttributes().stream().filter(a -> OWNER.equals(a.getName())).toList());
    }
    @GET @Path("users") public Object search(@QueryParam("email") String email, @QueryParam("username") String username,
                                            @QueryParam("exact") String exact) {
        if (!"true".equals(exact) || (email == null) == (username == null)
                || (email != null && email.length() > 320) || (username != null && username.length() > 320)) throw bad();
        return complete(() -> visibleMembers(groups.get("managed-users")).stream()
                .filter(u -> email != null ? email.equalsIgnoreCase(u.getEmail()) : username.equalsIgnoreCase(u.getUsername()))
                .map(this::userDto).toList());
    }
    @GET @Path("users/{id}") public Object user(@PathParam("id") String id) { return complete(() -> userDto(target(id, false))); }
    @GET @Path("users/{id}/groups") public Object userGroups(@PathParam("id") String id) {
        return complete(() -> target(id,false).getGroupsStream().map(this::groupDto).toList());
    }
    @GET @Path("users/{id}/role-mappings/realm") public Object directRoles(@PathParam("id") String id) {
        return complete(() -> target(id,false).getRealmRoleMappingsStream().map(this::roleDto).toList());
    }
    @GET @Path("users/{id}/role-mappings/realm/composite") public Object effectiveRoles(@PathParam("id") String id) {
        return complete(() -> { UserModel user = target(id,false); return realm.getRolesStream().filter(user::hasRole).map(this::roleDto).toList(); });
    }
    @POST @Path("users") public Response create(JsonNode body) {
        fields(body, Set.of("username", "email", "firstName", "lastName", "enabled", "emailVerified", "attributes", "groups", "requiredActions"));
        String ownership = owner(body);
        if (!body.path("groups").isArray() || body.path("groups").size() != 1 || !"/managed-users".equals(body.path("groups").get(0).asText())) throw forbidden();
        if (!body.path("enabled").isBoolean() || !body.path("enabled").asBoolean()
                || !body.path("emailVerified").isBoolean() || body.path("emailVerified").asBoolean()) throw forbidden();
        if (body.has("requiredActions")) {
            JsonNode actions = body.get("requiredActions");
            if (!actions.isArray() || actions.size() != 2 || !actions.get(0).isTextual() || !actions.get(1).isTextual()
                    || !new HashSet<>(List.of(actions.get(0).asText(), actions.get(1).asText())).equals(ACTIONS)) throw forbidden();
        }
        Map<String,List<String>> attributes = new HashMap<>();
        for (String field : List.of("username", "email", "firstName", "lastName")) {
            String value = text(body, field, field.equals("username") || field.equals("email"));
            if (value != null) attributes.put(field, List.of(value));
        }
        attributes.put(OWNER, List.of(ownership));
        return complete(() -> {
            try {
                if (session.users().getUserByUsername(realm, attributes.get("username").getFirst()) != null
                        || (!realm.isDuplicateEmailsAllowed() && session.users().getUserByEmail(realm, attributes.get("email").getFirst()) != null))
                    throw new ClientErrorException("Identity already exists",409);
                UserProfile profile = session.getProvider(UserProfileProvider.class).create(UserProfileContext.USER_API, attributes);
                profile.validate();
                UserModel user = profile.create();
                user.setEnabled(true); user.setEmailVerified(false);
                user.joinGroup(groups.get("managed-users"));
                ACTIONS.forEach(user::addRequiredAction);
                if (!authorized(user,true)) { session.getTransactionManager().setRollbackOnly(); throw forbidden(); }
                audit(OperationType.CREATE,user.getId());
                return Response.created(URI.create(session.getContext().getUri().getAbsolutePath() + "/" + user.getId())).build();
            } catch (ModelDuplicateException e) { throw new ClientErrorException("Identity already exists",409); }
              catch (ValidationException e) { throw bad(); }
        });
    }
    @PUT @Path("users/{id}") public Response update(@PathParam("id") String id, JsonNode body) {
        fields(body, Set.of("firstName", "lastName", "enabled", "attributes"));
        return complete(() -> {
            UserModel user = target(id,true);
            if (user.getEmail() == null) throw bad();
            if (body.has("attributes") && !owner(body).equals(user.getFirstAttribute(OWNER))) throw forbidden();
            if (body.has("enabled") && !body.get("enabled").isBoolean()) throw bad();
            Map<String,List<String>> attributes = new HashMap<>();
            attributes.put("username", List.of(user.getUsername())); attributes.put("email", List.of(user.getEmail()));
            attributes.put(OWNER, List.of(user.getFirstAttribute(OWNER)));
            for (String field : List.of("firstName", "lastName")) {
                String value = body.has(field) ? text(body,field,false) : field.equals("firstName") ? user.getFirstName() : user.getLastName();
                if (value != null) attributes.put(field, List.of(value));
            }
            try {
                UserProfile profile = session.getProvider(UserProfileProvider.class).create(UserProfileContext.USER_API, attributes, user);
                profile.validate(); profile.update(false);
                if (body.has("enabled")) user.setEnabled(body.get("enabled").asBoolean());
                if (!authorized(user,true)) { session.getTransactionManager().setRollbackOnly(); throw forbidden(); }
                audit(OperationType.UPDATE,user.getId());
                return Response.noContent().build();
            } catch (ValidationException e) { throw bad(); }
        });
    }
    private Response membership(String id, String groupId, boolean add) {
        return complete(() -> {
            UserModel user = target(id,true); GroupModel group = group(groupId);
            if (group.getId().equals(groups.get("managed-users").getId())) throw forbidden();
            if (add) user.joinGroup(group); else user.leaveGroup(group);
            if (!authorized(user,true)) { session.getTransactionManager().setRollbackOnly(); throw forbidden(); }
            audit(OperationType.UPDATE,user.getId());
            return Response.noContent().build();
        });
    }
    @PUT @Path("users/{id}/groups/{group}") public Response join(@PathParam("id") String id, @PathParam("group") String group) { return membership(id, group, true); }
    @DELETE @Path("users/{id}/groups/{group}") public Response leave(@PathParam("id") String id, @PathParam("group") String group) { return membership(id, group, false); }
    @POST @Path("users/{id}/logout") public Response logout(@PathParam("id") String id) {
        completeWrite(() -> {
            UserModel user = target(id,true);
            session.users().setNotBeforeForUser(realm,user,Time.currentTime());
            session.sessions().getUserSessionsStream(realm,user).toList().forEach(s -> AuthenticationManager.backchannelLogout(session,realm,s,
                    session.getContext().getUri(), session.getContext().getConnection(), session.getContext().getRequestHeaders(),true));
            audit(OperationType.ACTION,user.getId());
        });
        return Response.noContent().build();
    }
    @PUT @Path("users/{id}/execute-actions-email") public Response email(@PathParam("id") String id,
            @QueryParam("client_id") String clientId, @QueryParam("redirect_uri") String redirect,
            @QueryParam("lifespan") Integer lifespan, List<String> actions) {
        if (!"cleaning-suite-web".equals(clientId) || redirect == null || lifespan == null || lifespan < 300 || lifespan > 604800
                || actions == null || actions.size() > 2 || actions.stream().anyMatch(Objects::isNull)
                || new HashSet<>(actions).size() != actions.size() || !ACTIONS.containsAll(actions)) throw bad();
        return complete(() -> {
            UserModel user = target(id,true);
            UserResource.SendEmailParams checked = UserResource.verifySendEmailParams(session,realm,user,redirect,clientId,lifespan);
            int expiration = Time.currentTime() + checked.getLifespan();
            ExecuteActionsActionToken token = new ExecuteActionsActionToken(user.getId(),user.getEmail(),expiration,actions,checked.getRedirectUri(),checked.getClientId());
            String link = LoginActionsService.actionTokenProcessor(session.getContext().getUri())
                    .queryParam("key",token.serialize(session,realm,session.getContext().getUri())).build(realm.getName()).toString();
            try {
                session.getProvider(EmailTemplateProvider.class).setAttribute(Constants.TEMPLATE_ATTR_REQUIRED_ACTIONS,token.getRequiredActions())
                        .setRealm(realm).setUser(user).sendExecuteActions(link,TimeUnit.SECONDS.toMinutes(checked.getLifespan()));
                audit(OperationType.ACTION,user.getId());
                return Response.noContent().build();
            } catch (EmailException e) { throw new ServiceUnavailableException("Setup email delivery failed"); }
        });
    }
}
