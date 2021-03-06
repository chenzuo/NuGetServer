﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.Services;
using System.IO;
using System.Linq;
using System.ServiceModel.Activation;
using System.Web;
using System.Web.Hosting;
using System.Web.Mvc;
using System.Web.Routing;
using Castle.MicroKernel.Registration;
using Castle.Windsor;
using Ninject;
using NuGet.Server;
using NuGet.Server.DataServices;
using NuGet.Server.Infrastructure;

[assembly: WebActivator.PreApplicationStartMethod(typeof(NuGetServer.ApplicationBootstrapper), "Start")]


namespace NuGetServer {
    public class WindsorControllerFactory : DefaultControllerFactory {
        private readonly IWindsorContainer _container;

        public WindsorControllerFactory(IWindsorContainer container) {
            _container = container;
        }

        public override void ReleaseController(IController controller) {
            _container.Release(controller);
        }
    }

    public class WindsorControllerActivator : IControllerActivator {
        private readonly IWindsorContainer _container;

        public WindsorControllerActivator(IWindsorContainer container) {
            _container = container;
        }

        public IController Create(RequestContext requestContext, Type controllerType) {
            return (IController)_container.Resolve(controllerType);
        }
    }

    public class WindsorDependencyResolver : IDependencyResolver {
        private readonly IWindsorContainer _container;

        public WindsorDependencyResolver(IWindsorContainer container) {
            _container = container;
        }

        public object GetService(Type serviceType) {
            var all = ((IEnumerable<object>)_container.ResolveAll(serviceType)).ToList();
            if (all.Count == 0)
                return null;
            else if (all.Count == 1)
                return all[0];
            else
                throw new Exception("More than one type registered for the service " + serviceType.FullName);
        }

        public IEnumerable<object> GetServices(Type serviceType) {
            return (object[])_container.ResolveAll(serviceType);
        }
    }

    public static class ApplicationBootstrapper {
        public static IWindsorContainer Container { get; private set; }

        public static void Start() {
            MapRoutes(RouteTable.Routes);

            string usersDatabaseDirectory = ConfigurationManager.AppSettings["UsersDatabaseDirectory"] ?? "~/App_Data/Users";
            usersDatabaseDirectory = (usersDatabaseDirectory.StartsWith("~/") ? HostingEnvironment.MapPath(usersDatabaseDirectory) : Path.Combine(HostingEnvironment.ApplicationPhysicalPath, usersDatabaseDirectory));

            var authService = new UserRepository(usersDatabaseDirectory);

            Container = new WindsorContainer();
            Container.Register(
                Component.For<IWindsorContainer>().Instance(Container),
                AllTypes.FromThisAssembly().BasedOn<IController>().WithService.Self().LifestylePerWebRequest(),
                Component.For<IControllerFactory>().ImplementedBy<WindsorControllerFactory>(),
                Component.For<IControllerActivator>().ImplementedBy<WindsorControllerActivator>(),
                Component.For<IUserRepository>().Instance(authService)
            );

            DependencyResolver.SetResolver(new WindsorDependencyResolver(Container));

            authService.CreateAdminAccountIfNoUsersExist();
        }

        private static void MapRoutes(RouteCollection routes) {
            // The default route is http://{root}/nuget/Packages
            var factory = new DataServiceHostFactory();
            var serviceRoute = new ServiceRoute("nuget", factory, typeof(Packages));
            serviceRoute.Defaults = new RouteValueDictionary { { "serviceType", "odata" } };
            serviceRoute.Constraints = new RouteValueDictionary { { "serviceType", "odata" } };
            routes.Add("nuget", serviceRoute);

            routes.MapRoute(
                "Default", // Route name
                "{controller}/{action}/{id}", // URL with parameters
                new { controller = "Home", action = "Index", id = UrlParameter.Optional } // Parameter defaults
            );

            // For some godforsaken reason, the PublishPackage route (registered by NuGet.Server) causes the { controller, action } => url mapping to always return this route.
            routes.Remove(routes.OfType<Route>().Single(r => r.DataTokens != null && r.DataTokens.ContainsKey("__RouteName") && (string)r.DataTokens["__RouteName"] == "PublishPackage"));
        }
    }
}