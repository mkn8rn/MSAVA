using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using MSAVA_API.Authorization;
using MSAVA_INF.Contexts;

namespace MSAVA_API.Authentication;

internal sealed class PersistedJwtTokenValidator : JwtBearerEvents
{
    internal const string MissingBearerTokenFailure = "Authenticated request did not include a bearer token.";
    internal const string InactiveTokenFailure = "Bearer token is not active.";
    internal const string TokenStoreLookupFailure = "Failed to validate persisted bearer token.";

    private readonly Func<string, DateTime, CancellationToken, Task<bool>> _tokenLookup;
    private readonly TimeProvider _timeProvider;

    public PersistedJwtTokenValidator(BaseDataContext dataContext, TimeProvider timeProvider)
        : this(CreateTokenLookup(dataContext), timeProvider)
    {
    }

    internal PersistedJwtTokenValidator(
        Func<string, DateTime, CancellationToken, Task<bool>> tokenLookup,
        TimeProvider timeProvider)
    {
        _tokenLookup = tokenLookup ?? throw new ArgumentNullException(nameof(tokenLookup));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public override async Task TokenValidated(TokenValidatedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!BearerTokenHeader.TryRead(context.HttpContext.Request, out string tokenString))
        {
            context.Fail(MissingBearerTokenFailure);
            return;
        }

        DateTime utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        CancellationToken cancellationToken = context.HttpContext.RequestAborted;

        bool tokenIsActive;
        try
        {
            tokenIsActive = await _tokenLookup(tokenString, utcNow, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (RecoverableLookupFailurePolicy.IsRecoverable(ex))
        {
            context.Fail(TokenStoreLookupFailure);
            return;
        }

        if (!tokenIsActive)
            context.Fail(InactiveTokenFailure);
    }

    private static Func<string, DateTime, CancellationToken, Task<bool>> CreateTokenLookup(BaseDataContext dataContext)
    {
        ArgumentNullException.ThrowIfNull(dataContext);

        return (tokenString, utcNow, cancellationToken) => dataContext.Jwts
            .AsNoTracking()
            .AnyAsync(
                token => token.TokenString == tokenString &&
                    token.ExpiresAt > utcNow,
                cancellationToken);
    }
}
