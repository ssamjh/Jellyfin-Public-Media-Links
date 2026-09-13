using System;
using System.Linq;
using Jellyfin.Plugin.PublicMediaLinks.Security;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Net;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.PublicMediaLinks;

/// <summary>
/// Registers the plugin's services with the server.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        // Wraps Jellyfin's authorization so share tokens can authorise the HLS endpoints.
        // Direct play and the dashboard do not depend on this, so a failure here is logged
        // and swallowed rather than taking the whole plugin down with it.
        serviceCollection.Decorate<IAuthorizationContext, ShareTokenAuthorizationContext>();
    }
}

/// <summary>
/// Minimal service decoration support, since Microsoft.Extensions.DependencyInjection has no
/// built-in decorator and pulling in Scrutor would add a dependency for one method.
/// </summary>
internal static class ServiceCollectionDecoratorExtensions
{
    /// <summary>
    /// Replaces the registration of <typeparamref name="TService"/> with
    /// <typeparamref name="TDecorator"/>, passing the original implementation as the first
    /// constructor argument.
    /// </summary>
    /// <typeparam name="TService">The service being decorated.</typeparam>
    /// <typeparam name="TDecorator">The decorator.</typeparam>
    /// <param name="services">The service collection.</param>
    internal static void Decorate<TService, TDecorator>(this IServiceCollection services)
        where TService : class
        where TDecorator : class, TService
    {
        var descriptor = services.LastOrDefault(d => d.ServiceType == typeof(TService));

        if (descriptor is null)
        {
            // Jellyfin changed how it registers this service. Leaving the collection alone
            // means HLS links will not authorise, which fails closed.
            return;
        }

        var key = $"{typeof(TService).Name}+PublicMediaLinksInner";

        services.Add(CreateKeyedDescriptor(descriptor, key));

        var index = services.IndexOf(descriptor);
        services[index] = new ServiceDescriptor(
            typeof(TService),
            provider => ActivatorUtilities.CreateInstance<TDecorator>(
                provider,
                ((IKeyedServiceProvider)provider).GetRequiredKeyedService(typeof(TService), key)),
            descriptor.Lifetime);
    }

    private static ServiceDescriptor CreateKeyedDescriptor(ServiceDescriptor original, object key)
    {
        if (original.ImplementationInstance is not null)
        {
            return new ServiceDescriptor(original.ServiceType, key, original.ImplementationInstance);
        }

        if (original.ImplementationFactory is not null)
        {
            return new ServiceDescriptor(
                original.ServiceType,
                key,
                (provider, _) => original.ImplementationFactory(provider),
                original.Lifetime);
        }

        return new ServiceDescriptor(
            original.ServiceType,
            key,
            original.ImplementationType ?? throw new InvalidOperationException("Descriptor has no implementation."),
            original.Lifetime);
    }
}
