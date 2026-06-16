using System;
using System.Collections.Generic;
using System.Linq;
using Uno.Extensions.Navigation;
using MSAVA_App.Models;
using MSAVA_App.Presentation.Login;
using MSAVA_App.Presentation.Welcome;
using MSAVA_App.Services.Navigation;
using MSAVA_App.Presentation.FileManagement;

namespace MSAVA_App.Presentation;

// Routes are nested under the Shell root and render in Shell.xaml's region.
// ShellModel chooses the initial route after auth refresh; this map declares
// available views, their model types, and route guard metadata in one place.
// Keep view-model types unique so route lookup remains deterministic.
internal static class Routes
{
    private sealed record Node(string Path, Type ViewModel, bool IsDefault = false);

    public static void Register(IViewRegistry views, IRouteRegistry routes)
    {
        // Register views with their ViewModels
        views.Register(
            new ViewMap(ViewModel: typeof(ShellModel)),
            new ViewMap<LoginPage, LoginModel>(),
            new ViewMap<MainPage, MainModel>(),
            new ViewMap<FileManagementPage, FileManagementModel>()
        );

        // Register navigation guards/options per route
        NavigationService.RegisterFor<LoginModel>(new NavigationServiceOptions { Public = true });
        NavigationService.RegisterFor<MainModel>(new NavigationServiceOptions { Public = false });
        NavigationService.RegisterFor<FileManagementModel>(new NavigationServiceOptions { Public = false });

        // Build lookup for route construction from registered maps
        var byViewModel = new Dictionary<Type, ViewMap>
        {
            [typeof(ShellModel)] = views.FindByViewModel<ShellModel>(),
            [typeof(LoginModel)] = views.FindByViewModel<LoginModel>(),
            [typeof(MainModel)] = views.FindByViewModel<MainModel>(),
            [typeof(FileManagementModel)] = views.FindByViewModel<FileManagementModel>()
        };

        // Define routes nested under Shell
        Node[] rootChildren =
        [
            new("Login", typeof(LoginModel), IsDefault: true),
            new("Main", typeof(MainModel)),
            new("Files", typeof(FileManagementModel))
        ];

        // Build root route with nested children
        var children = rootChildren
            .Select(n => new RouteMap(n.Path, View: byViewModel[n.ViewModel], IsDefault: n.IsDefault))
            .ToArray();

        var root = new RouteMap("", View: byViewModel[typeof(ShellModel)], Nested: [ .. children ]);

        routes.Register(root);
    }
}
