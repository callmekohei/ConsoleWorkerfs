namespace Worker

(*

  see also
  - IHostApplicationLifetime  ( https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/generic-host?view=aspnetcore-8.0#ihostapplicationlifetime )
  - Hosted lifecycle services ( https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-8#hosted-lifecycle-services )

*)

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Configuration

module private WorkerHelpersUserCode =

  let userCodeOnStarted
    (logger : ILogger<_>)
    (cfg    : IConfiguration)
    = task {

          // 1.normal
          logger.LogWarning "Hello World!"

          // 2.error
          // failwith "my error!"

          // // 3.user cancel
          // do! async{
          //   while true do
          //     $"{DateTime.Now}" |> logger.LogInformation
          //     do! Async.Sleep 1000
          // }

    }


  let userCodeCleanUp
    (logger   : ILogger<_>)
    (cfg      : IConfiguration)
    (getError    : unit -> exn)
    (exitCode : int)
    = task {

        match exitCode with
        | 0 ->

          logger.LogDebug("clean up for normal!")

        | 1 ->

          logger.LogError(getError(), getError().Message)
          logger.LogDebug("clean up for error!")

        | _ ->

          logger.LogDebug("clean up for cancel!")
    }


module private WorkerHelpers =

  open WorkerHelpersUserCode

  let onStarted
    (logger         : ILogger<_>)
    (cfg            : IConfiguration)
    (appLifetime    : IHostApplicationLifetime)
    (updateError    : exn -> unit)
    (getExitCode    : unit -> Nullable<int>)
    (updateExitCode : Nullable<int> -> unit)
    = fun _ ->
        task {

          try

            try

              logger.LogDebug("Application has started")
              do! userCodeOnStarted logger cfg
              updateExitCode (Nullable(0))

            with ex ->

              updateError ex
              updateExitCode (Nullable(1))

          finally

            if getExitCode().HasValue
            then
              logger.LogDebug("call StopApplication()")
              appLifetime.StopApplication()

        } |> ignore


  let onStopping
    (logger      : ILogger<_>)
    (cfg         : IConfiguration)
    (cts         : CancellationTokenSource)
    (getExitCode : unit -> Nullable<int>)
    = fun _ ->

    // Ctrl+C will immediately come to this place.
    if getExitCode().HasValue |> not
    then
      logger.LogDebug("call StopApplication()")
      logger.LogDebug("call Cancel()")
      cts.Cancel()

    logger.LogDebug("Application is stopping...")


  let startAsync
    (logger         : ILogger<_>)
    (cfg            : IConfiguration)
    (appLifetime    : IHostApplicationLifetime)
    (ct             : CancellationToken)
    (updateError    : exn -> unit)
    (getExitCode    : unit -> Nullable<int>)
    (updateExitCode : Nullable<int> -> unit)
    = task {
      let mutable cts = Unchecked.defaultof<CancellationTokenSource>
      cts <- CancellationTokenSource.CreateLinkedTokenSource(ct)
      appLifetime.ApplicationStarted.Register  (onStarted  logger cfg appLifetime updateError getExitCode updateExitCode ) |> ignore
      appLifetime.ApplicationStopping.Register (onStopping logger cfg cts getExitCode) |> ignore
      return Task.CompletedTask
    }


  let stopAsync
    (logger      : ILogger<_>)
    (cfg         : IConfiguration)
    (ct          : CancellationToken)
    (getError    : unit -> exn)
    (getExitCode : unit -> Nullable<int>)
    = task {

        logger.LogDebug("Application is stoped!")

        // Exit code may be null if the user cancelled via Ctrl+C/SIGTERM
        Environment.ExitCode <- getExitCode().GetValueOrDefault(-1)
        logger.LogDebug($"Exiting with return code: {Environment.ExitCode}");

        // clean up
        do! userCodeCleanUp logger cfg getError Environment.ExitCode

        return Task.CompletedTask

    }


type Worker(
      logger      : ILogger<_>
    , cfg         : IConfiguration
    , appLifetime : IHostApplicationLifetime
  ) as this =

  [<DefaultValue>] val mutable error : exn
  let getError ()   = this.error
  let updateError x = this.error <- x

  [<DefaultValue>] val mutable exitCode : Nullable<int>
  let getExitCode ()   = this.exitCode
  let updateExitCode x = this.exitCode <- x

  interface IHostedService with
    member _.StartAsync (ct: CancellationToken) = WorkerHelpers.startAsync logger cfg appLifetime ct updateError getExitCode updateExitCode
    member _.StopAsync  (ct: CancellationToken) = WorkerHelpers.stopAsync  logger cfg ct getError getExitCode

  interface IHostedLifecycleService with
    member _.StartingAsync (ct: CancellationToken) = Task.CompletedTask
    member _.StartedAsync  (ct: CancellationToken) = Task.CompletedTask
    member _.StoppingAsync (ct: CancellationToken) = Task.CompletedTask
    member _.StoppedAsync  (ct: CancellationToken) = Task.CompletedTask