using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using MSAVA_INF.Contexts;
using MSAVA_Shared.Diagnostics;

namespace MSAVA_API.Authentication;

internal sealed class PersistedJwtTokenValidator : JwtBearerEvents
{
    internal const string MissingBearerTokenFailure = "Authenticated request did not include a bearer token.";
    internal const string InactiveTokenFailure = "Bearer token is not active.";
    internal const string TokenStoreLookupFailure = "Failed to validate persisted bearer token.";

    private readonly BaseDataContext _dataContext;
    private readonly TimeProvider _timeProvider;

    public PersistedJwtTokenValidator(BaseDataContext dataContext, TimeProvider timeProvider)
    {
        _dataContext = dataContext ?? throw new ArgumentNullException(nameof(dataContext));
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
            tokenIsActive = await _dataContext.Jwts
                .AsNoTracking()
                .AnyAsync(
                    token => token.TokenString == tokenString &&
                        token.ExpiresAt > utcNow,
                    cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (!CriticalExceptionPolicy.ContainsCriticalException(ex))
        {
            context.Fail(TokenStoreLookupFailure);
            return;
        }

        if (!tokenIsActive)
            context.Fail(InactiveTokenFailure);
    }
}
