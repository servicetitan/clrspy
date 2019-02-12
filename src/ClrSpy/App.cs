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
    public class FindProcessException : Exception
    {
        public FindProcessException(string m) : base(m) { }
    }

    [Command(Name = "ClrSpy", Description = "CLR Monitoring Tool")]
    [Subcommand(
        typeof(Stacks),
        typeof(ParallelStacks),
        typeof(Heap),
        typeof(TasksCommand),
        typeof(AllTasks),
        typeof(Handles)
        )]
    public class App
    {
        private static TimeSpan Timeout = TimeSpan.FromSeconds(5);

        private void OnExecute(CommandLineApplication app)
        {
            app.ShowHelp();
        }

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
                if (!int.TryParse(target, out var pid))
                {
                    var name = target.ToUpper();
                    var basename = !name.EndsWith(".EXE") ? name : name.Substring(0, name.Length - 4);
                    var processes = Process.GetProcesses().Where(o => o.ProcessName.ToUpper().Contains(basename)).ToArray();
                    var proc = processes.Length == 0 ? throw new FindProcessException($"Process {target} not found")
                        : processes.Length > 1 ? throw new FindProcessException($"Multiple processes match the specified name {target}")
                        : processes[0];
                    pid = proc.Id;
                }
                dataTarget = DataTarget.AttachToProcess(pid, (uint)Timeout.TotalMilliseconds, AttachFlag.NonInvasive);
            }
            if (dataTarget.ClrVersions.Count == 0)
                throw new FindProcessException("This process is not Managed");
            return dataTarget.ClrVersions[0].CreateRuntime();
        }

        [Command("stacks", Description = "Shows stack traces")]
        public class Stacks
        {
            [Argument(0, Description = "Process name, PID or dump filename")]
            private string Target { get; }

            [Option(Description = "Output as JSON", LongName = "json")]
            public bool Json { get; set; }

            private void OnExecute(IConsole console)
            {
                console.Out.WriteStacks(CallStacks.GetStackTraces(GetTargetRuntime(Target)), Json);
            }
        }

        [Command("pstacks",
            Description = "Shows parallel stacks, represented as a tree. "
                + "Stack traces merged by common part and sorted by number of threads sharing the same stack trace in descending order."
            )]
        public class ParallelStacks
        {
            [Argument(0, Description = "Process name, PID or dump filename")]
            private string Target { get; }

            [Option(Description = "Read JSON-serialized stacktraces from STDIN", LongName = "readjson")]
            public bool ReadJson { get; set; }

            private void OnExecute(IConsole console)
            {
                IEnumerable<IEnumerable<object>> chains = ReadJson ? CallStacks.ReadJsons(console.In) : CallStacks.GetStackTraces(GetTargetRuntime(Target));
                console.Out.WriteTree(Tree.MergeChains(chains));
            }
        }

        [Command("tasks", Description = "Shows list of ThreadPool and Timer tasks.")]
        public class TasksCommand
        {
            [Argument(0, Description = "Process name, PID or dump filename")]
            private string Target { get; }

            private void OnExecute(IConsole console)
            {
                console.Out.WriteLine("Tasks:\n");
                console.Out.WriteGroupedTasks(new TasksSpy(GetTargetRuntime(Target)).GetTasks());
            }
        }

        [Command("alltasks", Description = "Shows list of all tasks, found in the Heap.")]
        public class AllTasks
        {
            [Argument(0, Description = "Process name, PID or dump filename")]
            private string Target { get; }

            private void OnExecute(IConsole console)
            {
                console.Out.WriteLine("All Tasks:\n");
                console.Out.WriteGroupedTasks(new TasksSpy(GetTargetRuntime(Target)).GetAllTasks());
            }
        }

        [Command("handles", Description = "Shows list of all handles.")]
        public class Handles
        {
            [Argument(0, Description = "Process name, PID or dump filename")]
            private string Target { get; }

            private void OnExecute(IConsole console)
            {
                console.Out.WriteGroupedHandles(new HandleSpy(GetTargetRuntime(Target)).GetAllHandles());
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
                            case '0':
                            case '1':
                            case '2':
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
