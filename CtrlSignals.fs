namespace Workerfs

(*

  CtrlSignals.fs

    CTRL+CLOSE Signal                ( https://learn.microsoft.com/en-us/windows/console/ctrl-close-signal )
    HandlerRoutine callback function ( https://learn.microsoft.com/en-us/windows/console/handlerroutine )

*)

// This module manages the handling of control signals for the console application,
// such as CTRL+C, CTRL+BREAK, and console window closing signals.
module private CtrlSignals =

  open System.Runtime.InteropServices

  // Defines a private module for setting system parameters, although
  // setting the hung application timeout is not functional in this context.
  module private SystemParameters =

    // Imports the SystemParametersInfo function from user32.dll for interacting with system parameters.
    [<DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)>]
    extern bool SystemParametersInfo(
        uint  uiAction
      , uint  uiParam
      , uint& pvParam
      , uint  fWinIni)

    // Constants for getting and setting the hung application timeout value.
    let SPI_GETHUNGAPPTIMEOUT = 0x0078u
    let SPI_SETHUNGAPPTIMEOUT = 0x0079u
    let SPIF_UPDATEINIFILE    = 0x01u
    let SPIF_SENDCHANGE       = 0x02u

    // Retrieves the current hung application timeout value.
    let getHungAppTimeout () =
      let mutable timeout = 0u
      if SystemParametersInfo(SPI_GETHUNGAPPTIMEOUT, 0u, &timeout, 0u)
      then Some timeout
      else None

    // Sets a new hung application timeout value.
    let setHungAppTimeout (timeout: uint32) =
      let mutable nullAddress = 0u
      SystemParametersInfo(SPI_SETHUNGAPPTIMEOUT, timeout, &nullAddress , SPIF_UPDATEINIFILE ||| SPIF_SENDCHANGE) |> ignore

  // Enum defining the different types of control signals.
  type private CtrlTypes =
    | CTRL_C_EVENT        = 0 // CTRL+C signal
    | CTRL_BREAK_EVENT    = 1 // CTRL+BREAK signal
    | CTRL_CLOSE_EVENT    = 2 // Signal when the console window is closing
    | CTRL_LOGOFF_EVENT   = 5 // Signal when the user logs off ( received only by services )
    | CTRL_SHUTDOWN_EVENT = 6 // Signal when the system is shutting down (received only by services )

  // Delegate type for handling console control signals.
  type private ConsoleCtrlDelegate = delegate of CtrlTypes -> bool

  // Static scope instance of the delegate to ensure it's not garbage collected.
  let mutable private ctrlHandler: ConsoleCtrlDelegate = null

  // Defines how to handle various control signals.
  let private consoleCtrlHandler func (ctrlType: CtrlTypes) =

    match ctrlType with

    // Allows generic host to handle the signal.
    | CtrlTypes.CTRL_C_EVENT
    | CtrlTypes.CTRL_BREAK_EVENT
      -> false

    // Executes a custom function for these signals and returns true to halt further processing.
    | CtrlTypes.CTRL_CLOSE_EVENT
    | CtrlTypes.CTRL_LOGOFF_EVENT   (* received only by services *)
    | CtrlTypes.CTRL_SHUTDOWN_EVENT (* received only by services *)
    | _ ->
      int ctrlType
      |> fun n -> -n
      |> func
      |> Async.RunSynchronously
      true

  // Wraps the control signal handler within a delegate.
  let private ctrlSignalsHandler (cancelfunc: int -> Async<unit>) =
    ctrlHandler <- ConsoleCtrlDelegate(fun ctrlType -> consoleCtrlHandler cancelfunc ctrlType )
    ctrlHandler

  // Sets the custom control signal handler.
  [<DllImport("kernel32.dll")>]
  extern bool private SetConsoleCtrlHandler(ConsoleCtrlDelegate handler, bool add)

  // Public method to set the control signals handler.
  let setCtrlSignalsHandler cancelfunc =
    SetConsoleCtrlHandler(ctrlSignalsHandler cancelfunc , true) |> ignore