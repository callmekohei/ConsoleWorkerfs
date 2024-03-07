open System.Threading
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting

// Entry point for the application.
[<EntryPoint>]
let main args =

  // Creates a CancellationTokenSource to handle cancellation tokens.
  let cancellationTokenSource = new CancellationTokenSource()

  // Starts building the host for the application using default settings.
  Host.CreateDefaultBuilder(args)

    // Configures services for the application within the hosting environment.
    // Specifically, it adds a hosted service of type Workerfs.ConsoleWorkerfs to the service collection.
    .ConfigureServices(fun hostContext services ->
      services.AddHostedService<Workerfs.ConsoleWorkerfs>() |> ignore)

    // Enables console support for the application, allowing it to listen for Ctrl+C or SIGTERM signals for graceful shutdown.
    // This is effectively combining UseConsoleLifetime(), Build(), and RunAsync() methods to start the application and wait for shutdown signal.
    .RunConsoleAsync(cancellationTokenSource.Token)

  // Awaits the asynchronous operation to complete, effectively running the application.
  |> Async.AwaitTask
  |> Async.RunSynchronously

  // Returns 0 to indicate successful execution once the application shuts down.
  0