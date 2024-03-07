namespace Workerfs

(*

  ConsoleWorkerfs.fs

    exit code

       0 : normal
       1 : error
      -1 : cancel
      -2 : close
      -5 : log off  (received only by services)
      -6 : shutdown (received only by services)

*)

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Hosting

type ConsoleWorkerfs(logger: ILogger<ConsoleWorkerfs>, cfg:IConfiguration, appLifetime: IHostApplicationLifetime) as this =

    [<DefaultValue>] val mutable applicationTask : Task
    [<DefaultValue>] val mutable exitCode        : Nullable<int>
    [<DefaultValue>] val mutable alreadyCleanUp  : bool

    let errorAction (ex:exn) =
      match ex with
      // Ignore TaskCanceledException as it indicates the application is being shut down.
      | :? TaskCanceledException -> ()
      // OperationCanceledException is also ignored as it signifies a user-initiated cancellation.
      | :? OperationCanceledException -> ()
      | _ as ex ->
        logger.LogError(ex,ex.Message)
        this.exitCode <- Nullable(1) // 1:error

    interface IHostedLifecycleService with

      member _.StartingAsync(ct:CancellationToken) = task {

        let ctrlSignalHander n = async{

          if this.exitCode.HasValue |> not
          then
            this.exitCode <- Nullable(n) // -2:close -5:log off (received only by services) -6:shutdown (received only by services)
            appLifetime.StopApplication()

          // polling time is 1s
          while this.alreadyCleanUp |> not do
            do! Async.Sleep 1000

        }

        CtrlSignals.setCtrlSignalsHandler ctrlSignalHander

      }

        let appCts  = new CancellationTokenSource()
        let appTsk =
          async {
            try

              // (* 1.normal *)
              // logger.LogWarning "Hello World!"
              // this.exitCode <- Nullable(0)
              // appLifetime.StopApplication()

              (* 2.error *)
              // failwith "my error!"

              (* 3.user cancel *)
              while true do
                $"{DateTime.Now}" |> logger.LogInformation
                do! Async.Sleep 1000

            with e ->
              errorAction e
              appLifetime.StopApplication()
          }
          |> fun cmp -> Async.StartAsTask(computation=cmp,cancellationToken=appCts.Token)

        this.applicationTask <-  appTsk

        let registration = appLifetime.ApplicationStarted.Register(fun () ->
          appTsk |> ignore
        )

        let registration' = appLifetime.ApplicationStopping.Register( fun () ->
          // exitCode is null when ctrl + C
          if this.exitCode.HasValue |> not then this.exitCode <- Nullable(-1) // -1:cancel
          if isNull appCts |> not
          then
            try
              appCts.Cancel()
              appCts.Dispose()
            with e ->
              errorAction e
        )

        ct.Register(fun () -> registration.Dispose()) |> ignore
        ct.Register(fun () -> registration'.Dispose()) |> ignore


      }

      member _.StoppingAsync(ct:CancellationToken) = Task.CompletedTask
      member _.StopAsync(ct:CancellationToken)     = Task.CompletedTask
      member _.StoppedAsync(ct:CancellationToken)  = task {

        // Wait for the application logic to fully complete any cleanup tasks.
        // Note that this relies on the cancellation token to be properly used in the application.
        if isNull this.applicationTask |> not
        then
          with e -> errorAction e

        // cleanup
        match this.exitCode.Value with

        // NORMAL
        |  0 ->

          for _ in [1..10] do
            do! Async.Sleep 1000 // 1s
            logger.LogDebug("clean up for NORMAl!")

          this.alreadyCleanUp <- true

        // ERROR
        |  1 ->

          for _ in [1..10] do
            do! Async.Sleep 1000 // 1s
            logger.LogDebug("clean up for ERROR!")

          this.alreadyCleanUp <- true

        // CTRL C EVENT
        | -1 ->

          for _ in [1..10] do
            do! Async.Sleep 1000 // 1s
            logger.LogDebug("clean up for CANCEl!")

          this.alreadyCleanUp <- true

        // CTRL CLOSE EVENT(dafault time is 5s)
        | -2 ->

          while true do
            do! Async.Sleep 1000 // 1s
            logger.LogDebug("clean up for CLOSE!")

          this.alreadyCleanUp <- true

        // CTRL LOGOFF EVENT(dafault time is 5s , received only by services)
        | -5 ->

          while true do
            do! Async.Sleep 1000 // 1s
            logger.LogDebug("clean up for LOGOFF!")

          this.alreadyCleanUp <- true

        // CTRL SHUTDOWN EVENT(dafault time is 20s , received only by services)
        | -6 ->

          while true do
            do! Async.Sleep 1000 // 1s
            logger.LogDebug("clean up for SHUTDOWN!")

          this.alreadyCleanUp <- true

        | _ ->

          this.alreadyCleanUp <- true
          logger.LogDebug("clean up!")

      }