namespace Worker

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Configuration

type Worker(
    logger      : ILogger<Worker>
  , cfg         : IConfiguration
  , appLifetime : IHostApplicationLifetime
  ) as this =

  [<DefaultValue>] val mutable error    : exn
  [<DefaultValue>] val mutable exitCode : Nullable<int>

  interface IHostedService with

    member _.StartAsync(ct: CancellationToken): Task =

      async {

        let mutable cancellationTokenSource = Unchecked.defaultof<CancellationTokenSource>
        cancellationTokenSource <- CancellationTokenSource.CreateLinkedTokenSource(ct)

        appLifetime.ApplicationStarted.Register(fun _ ->

          async {

            logger.LogDebug("Application has started")

            try

              try

                // 1.normal
                logger.LogInformation "Hello World!"

                // 2.error
                // failwith "my error!"

                // 3.user cancel
                // do! async{
                //   while true do
                //     $"{DateTime.Now}" |> logger.LogInformation
                //     do! Async.Sleep 1000
                // }

                this.exitCode <- Nullable(0)

              with ex ->

                this.error <- ex
                this.exitCode <- Nullable(1)

            finally

              if this.exitCode.HasValue
              then
                logger.LogDebug("call StopApplication()")
                appLifetime.StopApplication()

          }
          |> fun x -> Async.Start(x,cancellationToken=cancellationTokenSource.Token)

        ) |> ignore

        appLifetime.ApplicationStopping.Register(fun _ ->

          // Ctrl+C will immediately come to this place.
          if this.exitCode.HasValue |> not
          then
            logger.LogDebug("call StopApplication()")
            logger.LogDebug("call Cancel()")
            cancellationTokenSource.Cancel()

          logger.LogDebug("Application is stopping...")

        ) |> ignore

        return Task.CompletedTask

      }
      |> fun x -> Async.StartAsTask(x, cancellationToken = ct)
      :> Task


    member _.StopAsync(ct: CancellationToken): Task =

      async {

        logger.LogDebug("Application is stoped!")

        // Exit code may be null if the user cancelled via Ctrl+C/SIGTERM
        Environment.ExitCode <- this.exitCode.GetValueOrDefault(-1)
        logger.LogDebug($"Exiting with return code: {Environment.ExitCode}");

        // clean up
        match Environment.ExitCode with
        | 0 ->
          logger.LogDebug("clean up for normal!")
        | 1 ->
          logger.LogError(this.error,this.error.Message)
          logger.LogDebug("clean up for error!")
        | _ ->
          logger.LogDebug("clean up for cancel!")

        return Task.CompletedTask

      }
      |> fun x -> Async.StartAsTask(x, cancellationToken = ct)
      :> Task