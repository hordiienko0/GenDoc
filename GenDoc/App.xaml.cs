using GenDoc.Data;
using GenDoc.Services;
using GenDoc.Services.Audit;
using GenDoc.Services.Generation;
using GenDoc.Services.Import;
using GenDoc.Services.Recipients;
using GenDoc.Services.Templates;
using GenDoc.ViewModels.Audit;
using GenDoc.ViewModels.Generation;
using GenDoc.ViewModels.Import;
using GenDoc.ViewModels.Login;
using GenDoc.ViewModels.Recipients;
using GenDoc.ViewModels.Settings;
using GenDoc.ViewModels.Shell;
using GenDoc.ViewModels.Templates;
using GenDoc.Views.Login;
using Microsoft.Extensions.DependencyInjection;
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
            services.AddDbContextFactory<AppDbContext>();

            services.AddTransient<IUserProfileService, UserProfileService>();
            services.AddTransient<IDatabaseSchemaInitializer, DatabaseSchemaInitializer>();
            services.AddTransient<IAuditLogService, AuditLogService>();
            services.AddTransient<IRecipientService, RecipientService>();
            services.AddSingleton<IDialogService, DialogService>();
            services.AddSingleton<IExportService, ExportService>();
            services.AddTransient<IImportService, ImportService>();
            services.AddTransient<IExportTemplateService, ExportTemplateService>();
            services.AddTransient<IAuditLogQueryService, AuditLogQueryService>();
            services.AddTransient<ITemplateService, TemplateService>();
            services.AddTransient<IDocumentGenerationService, DocumentGenerationService>();
            services.AddTransient<IGenerationService, GenerationService>();

            services.AddTransient<LoginViewModel>();
            services.AddTransient<LoginWindow>();
            services.AddTransient<MainViewModel>();
            services.AddTransient<RecipientsViewModel>();
            services.AddTransient<RecipientEditViewModel>();
            services.AddTransient<ImportViewModel>();
            services.AddTransient<TemplatesViewModel>();
            services.AddTransient<AuditLogViewModel>();
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<GenerationViewModel>();
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
            MainWindow = mainWindow;
            mainWindow.Show();

            ShutdownMode = ShutdownMode.OnMainWindowClose;
        }
    }

}
