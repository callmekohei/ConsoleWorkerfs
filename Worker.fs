namespace Worker

(*

  see also
  - IHostApplicationLifetime  ( https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/generic-host?view=aspnetcore-8.0#ihostapplicationlifetime )
  - Hosted lifecycle services ( https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-8#hosted-lifecycle-services )

*)

open System
open System.Collections.Concurrent
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Configuration

// WorkerUserCode: Defines user-specific code for the console application.
module private WorkerUserCode =

  let consoleApplicationAsync
    (logger : ILogger<_>)
    (cfg    : IConfiguration)
    = async {

      // (* 1.normal *)
      logger.LogWarning "Hello World!"

      (* 2.error *)
      // failwith "my error!"

      // (* 3.user cancel *)
      // do! async{
      //   while true do
      //     $"{DateTime.Now}" |> logger.LogInformation
      //     do! Async.Sleep 1000
      // }

    }

  let cleanUpAsync
    (logger   : ILogger<_>)
    (cfg      : IConfiguration)
    (getError : unit -> exn)
    (exitCode : int)
    = async {

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

  open WorkerUserCode

  let onStartedAsync
    (logger         : ILogger<_>)
    (cfg            : IConfiguration)
    (appLifetime    : IHostApplicationLifetime)
    (updateError    : exn -> unit)
    (updateExitCode : Nullable<int> -> unit)
    = async {

      try

        try

          logger.LogDebug("Application has started")

          // run user code
          do! consoleApplicationAsync logger cfg

          updateExitCode(Nullable(0))

        with
          | :? TaskCanceledException ->
            // Ignore TaskCanceledException as it indicates the application is being shut down.
            ()
          | :? OperationCanceledException ->
            // OperationCanceledException is also ignored as it signifies a user-initiated cancellation.
            ()
          | _ as ex ->
            updateError(ex)
            updateExitCode(Nullable(1))

      finally
        // Stop the application when the main logic is completed or an exception occurs.
        appLifetime.StopApplication()

      }

  let onStoppingAsync
    (logger      : ILogger<_>)
    (cfg         : IConfiguration)
    (getExitCode : unit -> Nullable<int>)
    = async {

      logger.LogDebug("Application is stopping...")

      // Ctrl+C will immediately come to this place.
      if getExitCode().HasValue |> not
      then
        logger.LogDebug("Canceling Tasks...")
        let! ct = Async.CancellationToken
        use cts = CancellationTokenSource.CreateLinkedTokenSource(ct)
        cts.Cancel()

    }

  let startAsync
    (logger         : ILogger<_>)
    (cfg            : IConfiguration)
    (ct             : CancellationToken)
    (appLifetime    : IHostApplicationLifetime)
    (updateError    : exn -> unit)
    (getExitCode    : unit -> Nullable<int>)
    (updateExitCode : Nullable<int> -> unit)
    : Task
    =
      async {

        let! ct' = Async.CancellationToken

        let onStared = fun () ->
          onStartedAsync logger cfg appLifetime updateError updateExitCode
          |> fun x -> Async.Start(x,cancellationToken=ct')
          |> ignore

        let onStopping = fun () ->
          onStoppingAsync logger cfg getExitCode
          |> fun x -> Async.Start(x,cancellationToken=ct')
          |> ignore

        appLifetime.ApplicationStarted.Register  <| onStared   |> ignore
        appLifetime.ApplicationStopping.Register <| onStopping |> ignore

        return Task.CompletedTask

      }
      |> fun x -> Async.StartAsTask(computation=x,cancellationToken=ct)
      :> Task

  let stopAsync
    (logger             : ILogger<_>)
    (cfg                : IConfiguration)
    (ct                 : CancellationToken)
    (getApplicationTask : unit -> bool * Task)
    (getError           : unit -> exn)
    (getExitCode        : unit -> Nullable<int>)
    : Task
    =
      async {

        // Exit code may be null if the user cancelled via Ctrl+C/SIGTERM
        Environment.ExitCode <- getExitCode().GetValueOrDefault(-1)
        logger.LogDebug($"Exiting with return code: {Environment.ExitCode}");

        // Wait for the application logic to fully complete any cleanup tasks.
        // Note that this relies on the cancellation token to be properly used in the application.
        let couldGetTask , applicationTask = getApplicationTask()
        if couldGetTask && (applicationTask.IsCompleted |> not )
        then do! applicationTask |> Async.AwaitTask

        logger.LogDebug("Application is stopped!")

        // clean up by user code
        do! cleanUpAsync logger cfg getError Environment.ExitCode

        return Task.CompletedTask

      }
      |> fun x -> Async.StartAsTask(computation=x,cancellationToken=ct)
      :> Task

type Worker(
      logger      : ILogger<_>
    , cfg         : IConfiguration
    , appLifetime : IHostApplicationLifetime
  ) as this =

  let dictApplicationTask = ConcurrentDictionary<string,Task>()
  let dictError           = ConcurrentDictionary<string, exn>()
  let dictExitCode        = ConcurrentDictionary<string, Nullable<int>>()

  member this.UpdateApplicationTask(task: Task) =
    dictApplicationTask.AddOrUpdate("applicationTask", task, (fun _ _ -> task)) |> ignore

  member this.UpdateError(e: exn) =
    dictError.AddOrUpdate("error", e, (fun _ _ -> e)) |> ignore

  member this.UpdateExitCode(code: Nullable<int>) =
    dictExitCode.AddOrUpdate("exitCode", code, (fun _ _ -> code)) |> ignore

  member this.GetApplicationTask() =
    dictApplicationTask.TryGetValue("applicationTask")

  member this.GetError() =
    let _, value = dictError.TryGetValue("error")
    value

  member this.GetExitCode() =
    let _, value = dictExitCode.TryGetValue("exitCode")
    value

  interface IHostedService with
    member _.StartAsync (ct: CancellationToken) =
      let applicationTask = WorkerHelpers.startAsync logger cfg ct appLifetime this.UpdateError this.GetExitCode this.UpdateExitCode
      this.UpdateApplicationTask(applicationTask)
      applicationTask

    member _.StopAsync  (ct: CancellationToken) =
      WorkerHelpers.stopAsync logger cfg ct this.GetApplicationTask this.GetError this.GetExitCode

  interface IHostedLifecycleService with
    member _.StartingAsync (ct: CancellationToken) = Task.CompletedTask
    member _.StartedAsync  (ct: CancellationToken) = Task.CompletedTask
    member _.StoppingAsync (ct: CancellationToken) = Task.CompletedTask
    member _.StoppedAsync  (ct: CancellationToken) = Task.CompletedTask