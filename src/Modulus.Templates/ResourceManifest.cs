namespace Modulus.Templates;

/// <summary>
/// Maps embedded resource names to logical template paths with token placeholders.
/// The logical paths use the <c>init/</c> or <c>module/</c> prefix for category filtering,
/// and <c>{{Token}}</c> placeholders that get replaced at runtime.
/// </summary>
/// <remarks>
/// Note: MSBuild converts hyphens to underscores in embedded resource names
/// (e.g. <c>building-blocks</c> becomes <c>building_blocks</c>).
/// </remarks>
internal static class ResourceManifest
{
    internal static readonly IReadOnlyDictionary<string, string> Entries = new Dictionary<string, string>
    {
        // ──────────────────────────────────────────────────────
        // Init / Solution
        // ──────────────────────────────────────────────────────
        ["Modulus.Templates.templates.init.solution.Directory.Build.props.template"]
            = "init/Directory.Build.props.template",
        ["Modulus.Templates.templates.init.solution.Directory.Packages.props.template"]
            = "init/Directory.Packages.props.template",
        ["Modulus.Templates.templates.init.solution._editorconfig.template"]
            = "init/.editorconfig.template",
        ["Modulus.Templates.templates.init.solution._gitignore.template"]
            = "init/.gitignore.template",
        ["Modulus.Templates.templates.init.solution.SolutionName.slnx.template"]
            = "init/{{SolutionName}}.slnx.template",

        // ──────────────────────────────────────────────────────
        // Init / Building Blocks / Domain
        // ──────────────────────────────────────────────────────
        ["Modulus.Templates.templates.init.building_blocks.domain.BuildingBlocks.Domain.csproj.template"]
            = "init/src/BuildingBlocks.Domain/BuildingBlocks.Domain.csproj.template",
        ["Modulus.Templates.templates.init.building_blocks.domain.Entities.Entity.cs.template"]
            = "init/src/BuildingBlocks.Domain/Entities/Entity.cs.template",
        ["Modulus.Templates.templates.init.building_blocks.domain.Entities.AggregateRoot.cs.template"]
            = "init/src/BuildingBlocks.Domain/Entities/AggregateRoot.cs.template",
        ["Modulus.Templates.templates.init.building_blocks.domain.Entities.IHasDomainEvents.cs.template"]
            = "init/src/BuildingBlocks.Domain/Entities/IHasDomainEvents.cs.template",
        ["Modulus.Templates.templates.init.building_blocks.domain.ValueObjects.ValueObject.cs.template"]
            = "init/src/BuildingBlocks.Domain/ValueObjects/ValueObject.cs.template",
        ["Modulus.Templates.templates.init.building_blocks.domain.DomainEvents.IDomainEvent.cs.template"]
            = "init/src/BuildingBlocks.Domain/DomainEvents/IDomainEvent.cs.template",
        ["Modulus.Templates.templates.init.building_blocks.domain.DomainEvents.DomainEvent.cs.template"]
            = "init/src/BuildingBlocks.Domain/DomainEvents/DomainEvent.cs.template",
        ["Modulus.Templates.templates.init.building_blocks.domain.Exceptions.DomainException.cs.template"]
            = "init/src/BuildingBlocks.Domain/Exceptions/DomainException.cs.template",
        ["Modulus.Templates.templates.init.building_blocks.domain.Identifiers.StronglyTypedId.cs.template"]
            = "init/src/BuildingBlocks.Domain/Identifiers/StronglyTypedId.cs.template",
        ["Modulus.Templates.templates.init.building_blocks.domain.Entities.IAuditable.cs.template"]
            = "init/src/BuildingBlocks.Domain/Entities/IAuditable.cs.template",

        // ──────────────────────────────────────────────────────
        // Init / Building Blocks / Application
        // ──────────────────────────────────────────────────────
        ["Modulus.Templates.templates.init.building_blocks.application.BuildingBlocks.Application.csproj.template"]
            = "init/src/BuildingBlocks.Application/BuildingBlocks.Application.csproj.template",
        ["Modulus.Templates.templates.init.building_blocks.application.IUnitOfWork.cs.template"]
            = "init/src/BuildingBlocks.Application/IUnitOfWork.cs.template",
        ["Modulus.Templates.templates.init.building_blocks.application.Behaviors.UnitOfWorkBehavior.cs.template"]
            = "init/src/BuildingBlocks.Application/Behaviors/UnitOfWorkBehavior.cs.template",
        ["Modulus.Templates.templates.init.building_blocks.application.DependencyInjection.ApplicationServiceExtensions.cs.template"]
            = "init/src/BuildingBlocks.Application/DependencyInjection/ApplicationServiceExtensions.cs.template",
        ["Modulus.Templates.templates.init.building_blocks.application.Persistence.IRepository.cs.template"]
            = "init/src/BuildingBlocks.Application/Persistence/IRepository.cs.template",
        ["Modulus.Templates.templates.init.building_blocks.application.Pagination.PagedResult.cs.template"]
            = "init/src/BuildingBlocks.Application/Pagination/PagedResult.cs.template",
        ["Modulus.Templates.templates.init.building_blocks.application.Pagination.PaginationQuery.cs.template"]
            = "init/src/BuildingBlocks.Application/Pagination/PaginationQuery.cs.template",

        // ──────────────────────────────────────────────────────
        // Init / Building Blocks / Infrastructure
        // ──────────────────────────────────────────────────────
        ["Modulus.Templates.templates.init.building_blocks.infrastructure.BuildingBlocks.Infrastructure.csproj.template"]
            = "init/src/BuildingBlocks.Infrastructure/BuildingBlocks.Infrastructure.csproj.template",
        ["Modulus.Templates.templates.init.building_blocks.infrastructure.Persistence.BaseDbContext.cs.template"]
            = "init/src/BuildingBlocks.Infrastructure/Persistence/BaseDbContext.cs.template",
        ["Modulus.Templates.templates.init.building_blocks.infrastructure.Outbox.OutboxConfiguration.cs.template"]
            = "init/src/BuildingBlocks.Infrastructure/Outbox/OutboxConfiguration.cs.template",
        ["Modulus.Templates.templates.init.building_blocks.infrastructure.Inbox.InboxConfiguration.cs.template"]
            = "init/src/BuildingBlocks.Infrastructure/Inbox/InboxConfiguration.cs.template",
        ["Modulus.Templates.templates.init.building_blocks.infrastructure.Inbox.InboxMessageConsumerConfiguration.cs.template"]
            = "init/src/BuildingBlocks.Infrastructure/Inbox/InboxMessageConsumerConfiguration.cs.template",
        ["Modulus.Templates.templates.init.building_blocks.infrastructure.Endpoints.IEndpoint.cs.template"]
            = "init/src/BuildingBlocks.Infrastructure/Endpoints/IEndpoint.cs.template",
        ["Modulus.Templates.templates.init.building_blocks.infrastructure.Outbox.OutboxMessageConsumer.cs.template"]
            = "init/src/BuildingBlocks.Infrastructure/Outbox/OutboxMessageConsumer.cs.template",
        ["Modulus.Templates.templates.init.building_blocks.infrastructure.Outbox.OutboxMessageConsumerConfiguration.cs.template"]
            = "init/src/BuildingBlocks.Infrastructure/Outbox/OutboxMessageConsumerConfiguration.cs.template",
        ["Modulus.Templates.templates.init.building_blocks.infrastructure.Outbox.IdempotentDomainEventHandler.cs.template"]
            = "init/src/BuildingBlocks.Infrastructure/Outbox/IdempotentDomainEventHandler.cs.template",
        ["Modulus.Templates.templates.init.building_blocks.infrastructure.Persistence.AuditableEntityInterceptor.cs.template"]
            = "init/src/BuildingBlocks.Infrastructure/Persistence/AuditableEntityInterceptor.cs.template",
        ["Modulus.Templates.templates.init.building_blocks.infrastructure.Persistence.EfRepository.cs.template"]
            = "init/src/BuildingBlocks.Infrastructure/Persistence/EfRepository.cs.template",
        ["Modulus.Templates.templates.init.building_blocks.infrastructure.Registration.IModuleRegistration.cs.template"]
            = "init/src/BuildingBlocks.Infrastructure/Registration/IModuleRegistration.cs.template",

        // ──────────────────────────────────────────────────────
        // Init / Building Blocks / Integration
        // ──────────────────────────────────────────────────────
        ["Modulus.Templates.templates.init.building_blocks.integration.BuildingBlocks.Integration.csproj.template"]
            = "init/src/BuildingBlocks.Integration/BuildingBlocks.Integration.csproj.template",
        ["Modulus.Templates.templates.init.building_blocks.integration.IntegrationEvents.IIntegrationEvent.cs.template"]
            = "init/src/BuildingBlocks.Integration/IntegrationEvents/IIntegrationEvent.cs.template",

        // ──────────────────────────────────────────────────────
        // Init / Host
        // ──────────────────────────────────────────────────────
        ["Modulus.Templates.templates.init.host.WebApi.csproj.template"]
            = "init/src/{{SolutionName}}.WebApi/{{SolutionName}}.WebApi.csproj.template",
        ["Modulus.Templates.templates.init.host.Program.cs.template"]
            = "init/src/{{SolutionName}}.WebApi/Program.cs.template",
        ["Modulus.Templates.templates.init.host.appsettings.json.template"]
            = "init/src/{{SolutionName}}.WebApi/appsettings.json.template",
        ["Modulus.Templates.templates.init.host.ModuleRegistration.cs.template"]
            = "init/src/{{SolutionName}}.WebApi/ModuleRegistration.cs.template",
        ["Modulus.Templates.templates.init.host.Middleware.GlobalExceptionHandler.cs.template"]
            = "init/src/{{SolutionName}}.WebApi/Middleware/GlobalExceptionHandler.cs.template",
        ["Modulus.Templates.templates.init.host.Extensions.ResultExtensions.cs.template"]
            = "init/src/{{SolutionName}}.WebApi/Extensions/ResultExtensions.cs.template",
        ["Modulus.Templates.templates.init.host.Extensions.ApiResults.cs.template"]
            = "init/src/{{SolutionName}}.WebApi/Extensions/ApiResults.cs.template",
        ["Modulus.Templates.templates.init.host.Extensions.ConfigurationExtensions.cs.template"]
            = "init/src/{{SolutionName}}.WebApi/Extensions/ConfigurationExtensions.cs.template",

        // ──────────────────────────────────────────────────────
        // Init / Tests
        // ──────────────────────────────────────────────────────
        ["Modulus.Templates.templates.init.tests.Tests.Common.csproj.template"]
            = "init/tests/{{SolutionName}}.Tests.Common/{{SolutionName}}.Tests.Common.csproj.template",
        ["Modulus.Templates.templates.init.tests.Tests.Architecture.csproj.template"]
            = "init/tests/{{SolutionName}}.Tests.Architecture/{{SolutionName}}.Tests.Architecture.csproj.template",
        ["Modulus.Templates.templates.init.tests.Tests.Integration.csproj.template"]
            = "init/tests/{{SolutionName}}.Tests.Integration/{{SolutionName}}.Tests.Integration.csproj.template",

        // ──────────────────────────────────────────────────────
        // Init / Aspire
        // ──────────────────────────────────────────────────────
        ["Modulus.Templates.templates.init.aspire.AppHost.csproj.template"]
            = "init/aspire/{{SolutionName}}.AppHost/{{SolutionName}}.AppHost.csproj.template",
        ["Modulus.Templates.templates.init.aspire.Program.cs.template"]
            = "init/aspire/{{SolutionName}}.AppHost/Program.cs.template",
        ["Modulus.Templates.templates.init.aspire.ServiceDefaults.csproj.template"]
            = "init/aspire/{{SolutionName}}.ServiceDefaults/{{SolutionName}}.ServiceDefaults.csproj.template",

        // ──────────────────────────────────────────────────────
        // Module / Src / Domain
        // ──────────────────────────────────────────────────────
        ["Modulus.Templates.templates.module.src.ModuleName.Domain.ModuleName.Domain.csproj.template"]
            = "module/src/{{ModuleName}}.Domain/{{ModuleName}}.Domain.csproj.template",
        ["Modulus.Templates.templates.module.src.ModuleName.Domain.AssemblyReference.cs.template"]
            = "module/src/{{ModuleName}}.Domain/AssemblyReference.cs.template",
        ["Modulus.Templates.templates.module.src.ModuleName.Domain._gitkeep"]
            = "module/src/{{ModuleName}}.Domain/.gitkeep",

        // ──────────────────────────────────────────────────────
        // Module / Src / Application
        // ──────────────────────────────────────────────────────
        ["Modulus.Templates.templates.module.src.ModuleName.Application.ModuleName.Application.csproj.template"]
            = "module/src/{{ModuleName}}.Application/{{ModuleName}}.Application.csproj.template",
        ["Modulus.Templates.templates.module.src.ModuleName.Application.AssemblyReference.cs.template"]
            = "module/src/{{ModuleName}}.Application/AssemblyReference.cs.template",
        ["Modulus.Templates.templates.module.src.ModuleName.Application.Samples.GetSampleQuery.cs.template"]
            = "module/src/{{ModuleName}}.Application/Samples/GetSampleQuery.cs.template",
        ["Modulus.Templates.templates.module.src.ModuleName.Application.Samples.GetSampleQueryHandler.cs.template"]
            = "module/src/{{ModuleName}}.Application/Samples/GetSampleQueryHandler.cs.template",
        ["Modulus.Templates.templates.module.src.ModuleName.Application.Data.IQueryDb.cs.template"]
            = "module/src/{{ModuleName}}.Application/Data/IQueryDb.cs.template",
        ["Modulus.Templates.templates.module.src.ModuleName.Application._gitkeep"]
            = "module/src/{{ModuleName}}.Application/.gitkeep",

        // ──────────────────────────────────────────────────────
        // Module / Src / Infrastructure
        // ──────────────────────────────────────────────────────
        ["Modulus.Templates.templates.module.src.ModuleName.Infrastructure.ModuleName.Infrastructure.csproj.template"]
            = "module/src/{{ModuleName}}.Infrastructure/{{ModuleName}}.Infrastructure.csproj.template",
        ["Modulus.Templates.templates.module.src.ModuleName.Infrastructure.AssemblyReference.cs.template"]
            = "module/src/{{ModuleName}}.Infrastructure/AssemblyReference.cs.template",
        ["Modulus.Templates.templates.module.src.ModuleName.Infrastructure.Persistence.ModuleNameDbContext.cs.template"]
            = "module/src/{{ModuleName}}.Infrastructure/Persistence/{{ModuleName}}DbContext.cs.template",
        ["Modulus.Templates.templates.module.src.ModuleName.Infrastructure.Persistence.ModuleNameReadOnlyDbContext.cs.template"]
            = "module/src/{{ModuleName}}.Infrastructure/Persistence/{{ModuleName}}ReadOnlyDbContext.cs.template",
        ["Modulus.Templates.templates.module.src.ModuleName.Infrastructure.ModuleNameModule.cs.template"]
            = "module/src/{{ModuleName}}.Infrastructure/{{ModuleName}}Module.cs.template",

        // ──────────────────────────────────────────────────────
        // Module / Src / Integration
        // ──────────────────────────────────────────────────────
        ["Modulus.Templates.templates.module.src.ModuleName.Integration.ModuleName.Integration.csproj.template"]
            = "module/src/{{ModuleName}}.Integration/{{ModuleName}}.Integration.csproj.template",
        ["Modulus.Templates.templates.module.src.ModuleName.Integration._gitkeep"]
            = "module/src/{{ModuleName}}.Integration/.gitkeep",

        // ──────────────────────────────────────────────────────
        // Module / Src / Api
        // ──────────────────────────────────────────────────────
        ["Modulus.Templates.templates.module.src.ModuleName.Api.ModuleName.Api.csproj.template"]
            = "module/src/{{ModuleName}}.Api/{{ModuleName}}.Api.csproj.template",
        ["Modulus.Templates.templates.module.src.ModuleName.Api.AssemblyReference.cs.template"]
            = "module/src/{{ModuleName}}.Api/AssemblyReference.cs.template",
        ["Modulus.Templates.templates.module.src.ModuleName.Api.Endpoints.ModuleNameEndpointRegistration.cs.template"]
            = "module/src/{{ModuleName}}.Api/Endpoints/{{ModuleName}}EndpointRegistration.cs.template",
        ["Modulus.Templates.templates.module.src.ModuleName.Api.Endpoints.GetSample.cs.template"]
            = "module/src/{{ModuleName}}.Api/Endpoints/GetSample.cs.template",
        ["Modulus.Templates.templates.module.src.ModuleName.Api._gitkeep"]
            = "module/src/{{ModuleName}}.Api/.gitkeep",

        // ──────────────────────────────────────────────────────
        // Module / Tests
        // ──────────────────────────────────────────────────────
        ["Modulus.Templates.templates.module.tests.ModuleName.Tests.Unit.ModuleName.Tests.Unit.csproj.template"]
            = "module/tests/{{ModuleName}}.Tests.Unit/{{ModuleName}}.Tests.Unit.csproj.template",
        ["Modulus.Templates.templates.module.tests.ModuleName.Tests.Integration.ModuleName.Tests.Integration.csproj.template"]
            = "module/tests/{{ModuleName}}.Tests.Integration/{{ModuleName}}.Tests.Integration.csproj.template",
        ["Modulus.Templates.templates.module.tests.ModuleName.Tests.Integration.ModuleNameIntegrationTestBase.cs.template"]
            = "module/tests/{{ModuleName}}.Tests.Integration/{{ModuleName}}IntegrationTestBase.cs.template",
        ["Modulus.Templates.templates.module.tests.ModuleName.Tests.Integration.ModuleNameEndpointTests.cs.template"]
            = "module/tests/{{ModuleName}}.Tests.Integration/{{ModuleName}}EndpointTests.cs.template",
        ["Modulus.Templates.templates.module.tests.ModuleName.Tests.Architecture.ModuleName.Tests.Architecture.csproj.template"]
            = "module/tests/{{ModuleName}}.Tests.Architecture/{{ModuleName}}.Tests.Architecture.csproj.template",
        ["Modulus.Templates.templates.module.tests.ModuleName.Tests.Architecture.LayerDependencyTests.cs.template"]
            = "module/tests/{{ModuleName}}.Tests.Architecture/LayerDependencyTests.cs.template",
    };
}
