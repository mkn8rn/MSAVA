using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using MSAVA_INF.Contexts;
using MSAVA_INF.Models;

namespace MSAVA_App.Tests;

public class BaseDataContextTests
{
    [Test]
    public void PublicDbSetPropertiesExposeExpectedEntityTypes()
    {
        using var context = CreateContext();
        var expectedEntityTypes = new Dictionary<string, Type>
        {
            [nameof(BaseDataContext.InviteCodes)] = typeof(InviteCodeDB),
            [nameof(BaseDataContext.Users)] = typeof(UserDB),
            [nameof(BaseDataContext.Jwts)] = typeof(JwtDB),
            [nameof(BaseDataContext.FileRefs)] = typeof(SavedFileReferenceDB),
            [nameof(BaseDataContext.FileData)] = typeof(SavedFileDataDB),
            [nameof(BaseDataContext.AccessCodes)] = typeof(AccessCodeDB),
            [nameof(BaseDataContext.AccessGroups)] = typeof(AccessGroupDB),
            [nameof(BaseDataContext.AccessLogs)] = typeof(AccessLogDB),
            [nameof(BaseDataContext.UserLogs)] = typeof(UserLogDB),
            [nameof(BaseDataContext.ErrorLogs)] = typeof(ErrorLogDB),
            [nameof(BaseDataContext.GroupLogs)] = typeof(GroupLogDB),
            [nameof(BaseDataContext.InviteLogs)] = typeof(InviteLogDB)
        };

        var dbSetProperties = typeof(BaseDataContext)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.PropertyType.IsGenericType)
            .Where(property => property.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
            .ToDictionary(
                property => property.Name,
                property => property.PropertyType.GenericTypeArguments[0]);

        dbSetProperties.Should().BeEquivalentTo(expectedEntityTypes);
        foreach (var (propertyName, entityType) in expectedEntityTypes)
        {
            typeof(BaseDataContext).GetProperty(propertyName)!.GetValue(context).Should().NotBeNull();
            context.Model.FindEntityType(entityType).Should().NotBeNull();
        }
    }

    [Test]
    public void OnModelCreatingKeepsUserOwnedRelationshipsRestrictDelete()
    {
        using var context = CreateContext();

        FindForeignKey(context.Model, typeof(AccessGroupDB), nameof(AccessGroupDB.OwnerId))
            .DeleteBehavior
            .Should()
            .Be(DeleteBehavior.Restrict);
        FindForeignKey(context.Model, typeof(UserDB), nameof(UserDB.InviteCodeId))
            .DeleteBehavior
            .Should()
            .Be(DeleteBehavior.Restrict);
        FindForeignKey(context.Model, typeof(InviteCodeDB), nameof(InviteCodeDB.OwnerId))
            .DeleteBehavior
            .Should()
            .Be(DeleteBehavior.Restrict);
    }

    [Test]
    public void OnModelCreatingKeepsFileDataOneToOneWithReference()
    {
        using var context = CreateContext();

        var fileReferenceForeignKey = FindForeignKey(
            context.Model,
            typeof(SavedFileDataDB),
            nameof(SavedFileDataDB.FileReferenceId));

        fileReferenceForeignKey.PrincipalEntityType.ClrType.Should().Be(typeof(SavedFileReferenceDB));
        fileReferenceForeignKey.IsUnique.Should().BeTrue();
        fileReferenceForeignKey.DeleteBehavior.Should().Be(DeleteBehavior.Restrict);
    }

    [Test]
    public void OnModelCreatingRestrictsFileAccessAndOwnershipDeletes()
    {
        using var context = CreateContext();

        var accessGroupForeignKey = FindForeignKey(
            context.Model,
            typeof(SavedFileReferenceDB),
            nameof(SavedFileReferenceDB.AccessGroupId));
        var creatorForeignKey = FindForeignKey(
            context.Model,
            typeof(SavedFileDataDB),
            nameof(SavedFileDataDB.OriginalCreator));
        var lastModifiedByForeignKey = FindForeignKey(
            context.Model,
            typeof(SavedFileDataDB),
            nameof(SavedFileDataDB.LastModifiedById));

        accessGroupForeignKey.PrincipalEntityType.ClrType.Should().Be(typeof(AccessGroupDB));
        accessGroupForeignKey.DeleteBehavior.Should().Be(DeleteBehavior.Restrict);

        creatorForeignKey.PrincipalEntityType.ClrType.Should().Be(typeof(UserDB));
        creatorForeignKey.DeleteBehavior.Should().Be(DeleteBehavior.Restrict);

        lastModifiedByForeignKey.PrincipalEntityType.ClrType.Should().Be(typeof(UserDB));
        lastModifiedByForeignKey.DeleteBehavior.Should().Be(DeleteBehavior.Restrict);
    }

    [Test]
    public void OnModelCreatingRestrictsJwtUserDeletes()
    {
        using var context = CreateContext();

        var jwtUserForeignKey = FindForeignKey(
            context.Model,
            typeof(JwtDB),
            nameof(JwtDB.UserId));

        jwtUserForeignKey.PrincipalEntityType.ClrType.Should().Be(typeof(UserDB));
        jwtUserForeignKey.DeleteBehavior.Should().Be(DeleteBehavior.Restrict);
    }

    [Test]
    public void OnModelCreatingRestrictsAuditLogDeletes()
    {
        using var context = CreateContext();

        AssertRestrictForeignKey(context.Model, typeof(AccessLogDB), nameof(AccessLogDB.UserId), typeof(UserDB));
        AssertRestrictForeignKey(context.Model, typeof(AccessLogDB), nameof(AccessLogDB.FileRefId), typeof(SavedFileReferenceDB));
        AssertRestrictForeignKey(context.Model, typeof(UserLogDB), nameof(UserLogDB.UserId), typeof(UserDB));
        AssertRestrictForeignKey(context.Model, typeof(UserLogDB), nameof(UserLogDB.AdminId), typeof(UserDB));
        AssertRestrictForeignKey(context.Model, typeof(GroupLogDB), nameof(GroupLogDB.UserId), typeof(UserDB));
        AssertRestrictForeignKey(context.Model, typeof(GroupLogDB), nameof(GroupLogDB.GroupId), typeof(AccessGroupDB));
        AssertRestrictForeignKey(context.Model, typeof(InviteLogDB), nameof(InviteLogDB.UserId), typeof(UserDB));
        AssertRestrictForeignKey(context.Model, typeof(InviteLogDB), nameof(InviteLogDB.InviteCodeId), typeof(InviteCodeDB));
        AssertRestrictForeignKey(context.Model, typeof(ErrorLogDB), nameof(ErrorLogDB.UserId), typeof(UserDB));
    }

    [Test]
    public void OnModelCreatingEnforcesUniqueUsernames()
    {
        using var context = CreateContext();

        var usernameIndex = context.Model
            .FindEntityType(typeof(UserDB))
            ?.GetIndexes()
            .SingleOrDefault(index =>
                index.Properties.Select(property => property.Name)
                    .SequenceEqual([nameof(UserDB.Username)]));

        usernameIndex.Should().NotBeNull();
        usernameIndex!.IsUnique.Should().BeTrue();
    }

    private static void AssertRestrictForeignKey(
        IModel model,
        Type entityType,
        string propertyName,
        Type principalEntityType)
    {
        var foreignKey = FindForeignKey(model, entityType, propertyName);

        foreignKey.PrincipalEntityType.ClrType.Should().Be(principalEntityType);
        foreignKey.DeleteBehavior.Should().Be(DeleteBehavior.Restrict);
    }

    [Test]
    public void OnModelCreatingEnforcesUniqueAccessGroupNamesPerOwner()
    {
        using var context = CreateContext();

        var ownerNameIndex = context.Model
            .FindEntityType(typeof(AccessGroupDB))
            ?.GetIndexes()
            .SingleOrDefault(index =>
                index.Properties.Select(property => property.Name)
                    .SequenceEqual([nameof(AccessGroupDB.OwnerId), nameof(AccessGroupDB.Name)]));

        ownerNameIndex.Should().NotBeNull();
        ownerNameIndex!.IsUnique.Should().BeTrue();
    }

    [Test]
    public void OnModelCreatingAppliesIdentityTextLengthLimits()
    {
        using var context = CreateContext();

        FindProperty(context.Model, typeof(UserDB), nameof(UserDB.Username))
            .GetMaxLength()
            .Should()
            .Be(UserDB.MaximumUsernameLength);

        FindProperty(context.Model, typeof(AccessGroupDB), nameof(AccessGroupDB.Name))
            .GetMaxLength()
            .Should()
            .Be(AccessGroupDB.MaximumNameLength);

        FindProperty(context.Model, typeof(JwtDB), nameof(JwtDB.Username))
            .GetMaxLength()
            .Should()
            .Be(UserDB.MaximumUsernameLength);
    }

    private static IForeignKey FindForeignKey(IModel model, Type entityType, string propertyName)
    {
        var entity = model.FindEntityType(entityType)
            ?? throw new InvalidOperationException($"Entity {entityType.Name} was not found in the EF model.");

        return entity
            .GetForeignKeys()
            .Single(foreignKey => foreignKey.Properties.Any(property => property.Name == propertyName));
    }

    private static IProperty FindProperty(IModel model, Type entityType, string propertyName)
    {
        var entity = model.FindEntityType(entityType)
            ?? throw new InvalidOperationException($"Entity {entityType.Name} was not found in the EF model.");

        return entity.FindProperty(propertyName)
            ?? throw new InvalidOperationException($"Property {entityType.Name}.{propertyName} was not found in the EF model.");
    }

    private static BaseDataContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<BaseDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new TestDataContext(options);
    }

    private sealed class TestDataContext : BaseDataContext
    {
        public TestDataContext(DbContextOptions<BaseDataContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<SavedFileDataDB>().Ignore(fileData => fileData.Metadata);
        }
    }
}
