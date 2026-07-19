using GenDoc.Data;
using GenDoc.Services;
using GenDoc.ViewModels.Login;
using GenDoc.Views.Login;
using Microsoft.Extensions.DependencyInjection;
using System.Configuration;
using System.Data;
using System.Windows;

namespace GenDoc
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public static IServiceProvider Services { get; private set; } = null!;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var services = new ServiceCollection();
            ConfigureServices(services);
            Services = services.BuildServiceProvider();

            RunLoginFlow();
        }

        private static void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<IDbPasswordProvider, InMemoryDbPasswordProvider>();
            services.AddSingleton<IDatabaseUnlockService, DatabaseUnlockService>();
            services.AddSingleton<ICurrentUserContext, CurrentUserContext>();
            services.AddTransient<AppDbContext>();

            services.AddTransient<IUserProfileService, UserProfileService>();

            services.AddTransient<LoginViewModel>();
            services.AddTransient<LoginWindow>();
            services.AddTransient<MainWindow>();
        }

        private void RunLoginFlow()
        {
            var loginWindow = Services.GetRequiredService<LoginWindow>();
            var loginSucceeded = loginWindow.ShowDialog() == true;

            if (!loginSucceeded)
            {
                Shutdown();
                return;
            }

            var mainWindow = Services.GetRequiredService<MainWindow>();
            mainWindow.Show();

            ShutdownMode = ShutdownMode.OnMainWindowClose;
        }
    }

}
