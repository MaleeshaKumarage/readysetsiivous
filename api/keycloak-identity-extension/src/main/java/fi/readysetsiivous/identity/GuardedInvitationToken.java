package fi.readysetsiivous.identity;

import java.util.List;
import org.keycloak.authentication.actiontoken.execactions.ExecuteActionsActionToken;

/** Signed native action token with an independent server-owned single-use ticket. */
public final class GuardedInvitationToken extends ExecuteActionsActionToken {
    public static final String TYPE = "cleaning-invitation";
    public GuardedInvitationToken() { super(null,0,List.of(),null,null); type(TYPE); }
    GuardedInvitationToken(String userId, String email, int expires, List<String> actions, String redirect, String client, String owner, String ticket, String epoch) {
        super(userId,email,expires,actions,redirect,client);
        type(TYPE); setNote("cleaning_owner",owner); setNote("cleaning_ticket",ticket); setNote("cleaning_epoch",epoch);
    }
}
