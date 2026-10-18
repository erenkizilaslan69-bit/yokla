using System.ComponentModel.DataAnnotations;
using BudgetApp.Infrastructure;
using BudgetApp.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace BudgetApp.Tests;

public class AppClockTests
{
    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "BudgetApp";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static IConfiguration Config(string? today) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [AppClock.TodaySetting] = today }).Build();

    [Fact]
    public void For_WithoutTheSettingIsTheSystemClock()
    {
        Assert.Same(TimeProvider.System, AppClock.For(Config(null), new Env("Development")));
        Assert.Same(TimeProvider.System, AppClock.For(Config(""), new Env("Production")));
    }

    [Fact]
    public void For_InDevelopmentPinsTheDate()
    {
        var clock = new AppClock(AppClock.For(Config("2026-10-15"), new Env("Development")));

        Assert.Equal(new DateTime(2026, 10, 15), clock.Today);
        Assert.Equal(clock.Today, clock.Now.Date);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Testing")]
    public void For_RefusesAPinnedDateOutsideDevelopment(string environment) =>
        Assert.Throws<InvalidOperationException>(() => AppClock.For(Config("2026-10-15"), new Env(environment)));

    [Theory]
    [InlineData("15.10.2026")]
    [InlineData("2026-13-01")]
    [InlineData("yarın")]
    public void For_RefusesADateItCannotRead(string value) =>
        Assert.Throws<InvalidOperationException>(() => AppClock.For(Config(value), new Env("Development")));

    [Fact]
    public void FixedDateTimeProvider_KeepsTheDateAndTheRealTimeOfDay()
    {
        var real = new FakeTime(new DateTime(2026, 10, 19, 21, 30, 5));
        var fixedClock = new FixedDateTimeProvider(new DateTime(2026, 10, 15), real);

        Assert.Equal(new DateTime(2026, 10, 15, 21, 30, 5), fixedClock.GetLocalNow().DateTime);
    }

    [Fact]
    public void NotInFuture_ComparesWithTheAppsClock()
    {
        var clock = new AppClock(new FixedDateTimeProvider(new DateTime(2026, 10, 15)));
        var services = new ServiceProviderStub(clock);
        var attribute = new NotInFutureAttribute();

        ValidationResult? Check(DateTime date) =>
            attribute.GetValidationResult(date, new ValidationContext(new object(), services, null) { MemberName = "Date" });

        Assert.Equal(ValidationResult.Success, Check(new DateTime(2026, 10, 15)));
        var error = Check(new DateTime(2026, 10, 16));
        Assert.Equal("Tarih bugünden ileri olamaz.", error!.ErrorMessage);
        Assert.Equal(["Date"], error.MemberNames);
    }

    private sealed class FakeTime(DateTime localNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(localNow, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class ServiceProviderStub(AppClock clock) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(AppClock) ? clock : null;
    }
}
