open System.Threading
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting

[<EntryPoint>]
let main args =

  let cancellationTokenSource = new CancellationTokenSource()

  Host.CreateDefaultBuilder(args)

    .ConfigureServices(fun hostContext services ->
      services.AddHostedService<Workerfs.ConsoleWorkerfs>() |> ignore)

    // Enables console support, builds and starts the host, and waits for Ctrl+C or SIGTERM to shut down.
    .UseConsoleLifetime()
    .RunConsoleAsync(cancellationTokenSource.Token)

  |> Async.AwaitTask
  |> Async.RunSynchronously

  0