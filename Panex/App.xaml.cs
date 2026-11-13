using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Panex.Services;
using Panex.ViewModels;
using System;

namespace Panex
{
    public partial class App : Application
    {
        private Window? m_window;

        // Expose the ServiceProvider so ViewModels created by XAML can resolve dependencies
        public IServiceProvider Services { get; }
        public new static App Current => (App)Application.Current;

        public App()
        {
            this.InitializeComponent();
            Services = ConfigureServices();
        }

        private IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            services.AddSingleton<PeriodicTableService>();
            services.AddSingleton<BuildupCalculator>();

            services.AddSingleton<PhotoatomicDataService>(); // EPDL Reader
            services.AddSingleton<XcomDataService>();        // XCOM Reader

            services.AddTransient<MaterialLibraryViewModel>();
            services.AddTransient<ChartViewModel>();
            services.AddTransient<DataTableViewModel>();

            return services.BuildServiceProvider();
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            m_window = new MainWindow();
            m_window.Activate();
        }
    }
}