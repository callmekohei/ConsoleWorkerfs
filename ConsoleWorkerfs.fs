namespace Workerfs

// ConsoleWorkerfs.fs

type ExitCode =
  | Normal   = 0
  | Error    = 1
  | Cancel   = -1
  | Close    = 2
  | LogOff   = 5  // received only by services
  | Shutdown = 6  // received only by services

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Hosting

// This module manages the handling of control signals for the console application,
// such as CTRL+C, CTRL+BREAK, and console window closing signals.
module private CtrlSignals =
  open System.Runtime.InteropServices

  // Enum defining the different types of control signals.
  // HandlerRoutine callback function : https://learn.microsoft.com/en-us/windows/console/handlerroutine
  type CtrlTypes =
    | CTRL_C_EVENT        = 0 // CTRL+C signal
    | CTRL_BREAK_EVENT    = 1 // CTRL+BREAK signal
    | CTRL_CLOSE_EVENT    = 2 // Signal when the console window is closing
    | CTRL_LOGOFF_EVENT   = 5 // Signal when the user logs off ( received only by services )
    | CTRL_SHUTDOWN_EVENT = 6 // Signal when the system is shutting down (received only by services )

  // Delegate type for handling console control signals.
  type private ConsoleCtrlDelegate = delegate of CtrlTypes -> bool

  // Sets the custom control signal handler.
  [<DllImport("kernel32.dll")>]
  extern bool private SetConsoleCtrlHandler(ConsoleCtrlDelegate handler, bool add)

  // Public method to set the control signals handler.
  let setCtrlSignalsHandler cancelfunc =
    // Create the delegate directly with a lambda expression
    let handler = ConsoleCtrlDelegate(fun ctrlType ->
        match ctrlType with
        // Let generic host handle the signal
        | CtrlTypes.CTRL_C_EVENT | CtrlTypes.CTRL_BREAK_EVENT ->
          false
        // Directly call the provided async function and run it synchronously
        // Halt further processing
        | _ ->
          ctrlType |> cancelfunc |> Async.RunSynchronously
          true
    )

    // Set the custom control signal handler using the created delegate
    SetConsoleCtrlHandler(handler, true) |> ignore


// Defines the ConsoleWorkerfs class for handling the application's lifecycle and cleanup.
type ConsoleWorkerfs(logger: ILogger<ConsoleWorkerfs>, cfg:IConfiguration, appLifetime: IHostApplicationLifetime) as this =

    // Initializes default mutable fields.
    [<DefaultValue>] val mutable applicationCts  : CancellationTokenSource
    [<DefaultValue>] val mutable applicationTask : Task
    [<DefaultValue>] val mutable exitCode        : Nullable<ExitCode>
    [<DefaultValue>] val mutable alreadyCleanUp  : bool

    // Creates a new CancellationTokenSource upon instantiation.
    do this.applicationCts <- new CancellationTokenSource()

    // Handles exceptions uniformly, setting appropriate exit codes and logging errors.
    let errorAction (ex:exn) =
      match ex with
      // Handles cancellation-related exceptions by setting the exit code to -1 (cancel).
      | :? TaskCanceledException | :? OperationCanceledException -> if this.exitCode.HasValue |> not then this.exitCode <- Nullable(ExitCode.Cancel)
      // Logs other exceptions and sets the exit code to 1 (error).
      | _ as ex ->
        logger.LogError(ex,ex.Message)
        this.exitCode <- Nullable(ExitCode.Error)

    // Implements IDisposable for resource cleanup.
    interface IDisposable with
      member this.Dispose() =
        // Disposes of the CancellationTokenSource safely.
        if isNull this.applicationCts |> not
        then
          try this.applicationCts.Dispose()
          with e -> logger.LogError($"Exception during applicationCts disposal: {e.Message}")
          this.applicationCts <- null

    // Implements IHostedLifecycleService for managing the application's lifecycle.
    interface IHostedLifecycleService with

      // Prepares the service for starting, including setting up cancellation tokens and exit codes.
      member _.StartingAsync(ct:CancellationToken) = task {

        let registration = appLifetime.ApplicationStopping.Register( fun () ->
          // exitCode is null when ctrl + C
          if this.exitCode.HasValue |> not then this.exitCode <- Nullable(ExitCode.Cancel)
          if isNull this.applicationCts |> not
          then
            try this.applicationCts.Cancel()
            with e -> errorAction e
        )

        ct.Register(fun () -> registration.Dispose()) |> ignore

        // Handles specific shutdown signals, applying relevant exit codes.
        let ctrlSignalHander (ctrlTypes:CtrlSignals.CtrlTypes) = async{

          let exitCode =
            match ctrlTypes with
            | CtrlSignals.CtrlTypes.CTRL_CLOSE_EVENT    -> ExitCode.Close
            | CtrlSignals.CtrlTypes.CTRL_LOGOFF_EVENT   -> ExitCode.LogOff
            | CtrlSignals.CtrlTypes.CTRL_SHUTDOWN_EVENT -> ExitCode.Shutdown
            | _ -> ExitCode.Cancel

          if this.exitCode.HasValue |> not
          then
            this.exitCode <- Nullable(exitCode)
            appLifetime.StopApplication()

          while this.alreadyCleanUp |> not do
            do! Async.Sleep 1000 // polling time is 1s

        }

        CtrlSignals.setCtrlSignalsHandler ctrlSignalHander

      }

      // Placeholder for the service's start logic; might include configuration settings or initialization tasks.
      member _.StartAsync(ct:CancellationToken) = task {
        try
          () // Add initialization logic here.
        with e ->
          errorAction e
          appLifetime.StopApplication()
      }

      // Contains the main logic to be executed once the service has started.
      member _.StartedAsync(ct:CancellationToken) = task {

        if this.exitCode.HasValue
        then return Task.CompletedTask
        else

          // Add primary task execution logic here.
          this.applicationTask <-
            async {
              try

                let! ct = Async.CancellationToken

                // (* 1.normal *)
                // logger.LogWarning "Hello World!"
                // this.exitCode <- Nullable(0) // 0:normal
                // appLifetime.StopApplication()

                (* 2.error *)
                // failwith "my error!"

                (* 3.user cancel *)
                while ct.IsCancellationRequested |> not do
                  $"{DateTime.Now}" |> logger.LogInformation
                  do! Async.Sleep 1000

              with e ->
                errorAction e
                appLifetime.StopApplication()

            }
            |> fun cmp -> Async.StartAsTask(computation=cmp,cancellationToken=this.applicationCts.Token)

          let registration = appLifetime.ApplicationStarted.Register(fun () ->
            this.applicationTask |> ignore
          )

          ct.Register(fun () -> registration.Dispose()) |> ignore

          return Task.CompletedTask

      }

      // Defines actions to be taken when the service is stopping.
      member _.StoppingAsync(ct:CancellationToken) = Task.CompletedTask

      // Defines actions to be taken after the service has stopped.
      member _.StopAsync(ct:CancellationToken)     = Task.CompletedTask

      // Cleans up resources and performs final actions after the service has completely stopped.
      member _.StoppedAsync(ct:CancellationToken)  = task {

        // Wait for the application logic to fully complete any cleanup tasks.
        // Note that this relies on the cancellation token to be properly used in the application.
        if isNull this.applicationTask |> not
        then
          try do! this.applicationTask
          with e -> errorAction e

        // Matches the exit code to determine the appropriate cleanup actions.
        match this.exitCode.Value with

        // NORMAL exit
        |  ExitCode.Normal ->

          for _ in [1..10] do
            do! Async.Sleep 1000 // 1s
            logger.LogDebug("clean up for NORMAl!")

          this.alreadyCleanUp <- true

        // Error exit
        |  ExitCode.Error ->

          for _ in [1..10] do
            do! Async.Sleep 1000 // 1s
            logger.LogDebug("clean up for ERROR!")

          this.alreadyCleanUp <- true

        // Cancelled
        | ExitCode.Cancel ->

          for _ in [1..10] do
            do! Async.Sleep 1000 // 1s
            logger.LogDebug("clean up for CANCEl!")

          this.alreadyCleanUp <- true

        // Closed (dafault time is 5s)
        | ExitCode.Close ->

          while true do
            do! Async.Sleep 1000 // 1s
            logger.LogDebug("clean up for CLOSE!")

          this.alreadyCleanUp <- true

        // Logoff (dafault time is 5s , received only by services)
        | ExitCode.LogOff ->

          while true do
            do! Async.Sleep 1000 // 1s
            logger.LogDebug("clean up for LOGOFF!")

          this.alreadyCleanUp <- true

        // Shutdown (dafault time is 20s , received only by services)
        | ExitCode.Shutdown ->

          while true do
            do! Async.Sleep 1000 // 1s
            logger.LogDebug("clean up for SHUTDOWN!")

          this.alreadyCleanUp <- true

        // Ohter cases
        | _ ->

          this.alreadyCleanUp <- true
          logger.LogDebug("clean up!")

      }