using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Cronos;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Diagnostics.Runtime;
using Serilog;


namespace ClrSpy
{
    [Command(Name = "ClrSpy", Description = "CLR Monitoring Tool")]
    [Subcommand(typeof(ParallelStacks), typeof(Heap))]
    public class App
    {
        private static TimeSpan Timeout = TimeSpan.FromSeconds(5);

        public static async Task<int> Main(string[] args)
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT) {
                Console.OutputEncoding = Encoding.UTF8;
            }

            Serilog.Log.Logger = new LoggerConfiguration()
                         .MinimumLevel.Debug()
                         .WriteTo.Console(Serilog.Events.LogEventLevel.Debug,
                                          outputTemplate: "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                         .CreateLogger();

            try {
                return await CommandLineApplication.ExecuteAsync<App>(args);
            }
            catch (TargetInvocationException ex) {
                Console.Error.WriteLine(ex.InnerException.Message);
                return 1;
            }
            catch (Exception ex) {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        private static ClrRuntime GetTargetRuntime(string target)
        {
            DataTarget dataTarget = null;
            if (Path.GetExtension(target)?.ToUpper() == ".DMP") {
                dataTarget = DataTarget.LoadCrashDump(target);
            }
            else if (target.StartsWith("core.")) {
                dataTarget = DataTarget.LoadCoreDump(target);
            }
            else {
                int pid = int.TryParse(target, out var a) ? a : (Process.GetProcessesByName(target).FirstOrDefault()?.Id ?? throw new Exception($"Process '{target}' not found"));
                dataTarget = DataTarget.AttachToProcess(pid, (uint)Timeout.TotalMilliseconds, AttachFlag.NonInvasive);
            }
            return dataTarget.ClrVersions[0].CreateRuntime();
        }

        [Command("pstacks", Description = "Shows parallel stacks")]
        public class ParallelStacks
        {
            [Argument(0, Description = "Process name, PID or dump filename")]
            private string Target { get; }

            private void OnExecute(IConsole console)
            {
                var runtime = GetTargetRuntime(Target);
                console.Out.WriteLine("Parallel Stacks:\n");
                console.Out.WriteTree(Tree.MergeChains(CallStacks.GetStackTraces(runtime)));
            }
        }

        [Command("heap", Description = "Analyze Heap")]
        public class Heap
        {
            [Argument(0, Description = "Process name, PID or dump filename")]
            private string Target { get; }

            [Option(Description = "Cron schedule", LongName = "schedule", ShortName = "s", ShowInHelpText = true)]
            public string Schedule { get; set; }

            [Option(Description = "Output filename template. Default - don't create output files",
                LongName = "output",
                ShortName = "o",
                ShowInHelpText = true)]
            public string OutputFilenameTemplate { get; set; }

            [Option(Description = "GC Gen to collect. Default - 'gen0, gen1, gen2'",
                LongName = "gen",
                ShortName = "g",
                ShowInHelpText = true)]
            public string GcGenerationsToCollect { get; set; } = "gen0, gen1, gen2";

            [Option(Description = "Count of diff to display, default = -1, disabled, 0 - without limit",
                LongName = "top",
                ShortName = "t",
                ShowInHelpText = true)]
            public int PrintDiffLimit { get; set; } = -1;

            private async Task OnExecuteAsync()
            {
                var closingAppCts = new CancellationTokenSource();
                Init(closingAppCts);
                var serviceCollection = new ServiceCollection()
                    .AddLogging(b => b.AddSerilog());

                using (var serviceProvider = serviceCollection.BuildServiceProvider())
                using (var scope = serviceProvider.CreateScope()) {
                    var gensStr = GcGenerationsToCollect.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    var listGenToConnect = new List<int>(3);
                    for (int i = 0; i < gensStr.Length; ++i) {
                        var ch = gensStr[i].Trim().Last();
                        switch (ch) {
                            case '1':
                            case '2':
                            case '3':
                                listGenToConnect.Add(Convert.ToInt32(ch.ToString()));
                                break;
                            default: {
                                    Log.Logger.Fatal("Can't parse '--gen' options. Input: ''", gensStr[i]);
                                    Environment.Exit(-1);
                                }
                                break;
                        }
                    }
                    Log.Logger.Information("Started");
                    var spyConfig = new ClrSpyConfiguration {
                        DatetimeAccessor = () => DateTimeOffset.Now,
                        Runtime = GetTargetRuntime(Target),
                        OutputFilenameTemplate = OutputFilenameTemplate,
                        PrintDiffLimit = PrintDiffLimit,
                        Schedule = string.IsNullOrWhiteSpace(Schedule)
                                                ? null
                                                : CronExpression.Parse(Schedule, CronFormat.IncludeSeconds)
                    };
                    foreach (var gen in listGenToConnect) {
                        spyConfig.GcGenToCollect[gen] = true;
                    }
                    var guard = ActivatorUtilities.CreateInstance<ClrSpy>(scope.ServiceProvider, spyConfig);
                    await guard.StartAsync(closingAppCts.Token);
                }
            }

            private static void Init(CancellationTokenSource closingAppCts)
            {
                Environment.CurrentDirectory = Path.GetDirectoryName(typeof(App).Assembly.Location);

                Log.Logger.Information("Press `Ctrl + C` or Enter to exit...");

                Console.CancelKeyPress += (s, e) => {
                    e.Cancel = true;
                    Log.Logger.Information("Stop executing. Wait...");
                    closingAppCts.Cancel();
                };
            }

        }
    }
}
