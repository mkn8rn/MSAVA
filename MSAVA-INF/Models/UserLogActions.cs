using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MSAVA_INF.Models
{
    public enum UserLogAction : short
    {
        Unknown = 0,

        // Sessions and Logins
        NewSession = 1,
        SessionLogIn = 2,
        SessionLogOut = 3,
        SessionExpired = 4,
        SessionRefreshed = 5,

        // Credential changes
        PasswordChange = 100,
        PasswordChangeByAdmin = 101,
        UsernameChange = 102,
        UsernameChangeByAdmin = 103,

        // Account changes
        AccountCreation = 200,
        AccountRegistered = 201,
        AccountDeletion = 202,
        AccountBanned = 203,
        AccountUnbanned = 204
    }
}
