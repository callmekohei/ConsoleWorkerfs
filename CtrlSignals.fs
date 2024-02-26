namespace Workerfs

(*

  CtrlSignals.fs

    CTRL+CLOSE Signal                ( https://learn.microsoft.com/en-us/windows/console/ctrl-close-signal )
    HandlerRoutine callback function ( https://learn.microsoft.com/en-us/windows/console/handlerroutine )

*)

module private CtrlSignals =

  open System.Runtime.InteropServices

  // setHungAppTimeout is not work...
  module private SystemParameters =

    [<DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)>]
    extern bool SystemParametersInfo(
        uint  uiAction
      , uint  uiParam
      , uint& pvParam
      , uint  fWinIni)

    let SPI_GETHUNGAPPTIMEOUT = 0x0078u
    let SPI_SETHUNGAPPTIMEOUT = 0x0079u
    let SPIF_UPDATEINIFILE    = 0x01u
    let SPIF_SENDCHANGE       = 0x02u

    let getHungAppTimeout () =
      let mutable timeout = 0u
      if SystemParametersInfo(SPI_GETHUNGAPPTIMEOUT, 0u, &timeout, 0u)
      then Some timeout
      else None

    let setHungAppTimeout (timeout: uint32) =
      let mutable nullAddress = 0u
      SystemParametersInfo(SPI_SETHUNGAPPTIMEOUT, timeout, &nullAddress , SPIF_UPDATEINIFILE ||| SPIF_SENDCHANGE) |> ignore


  type private CtrlTypes =
    | CTRL_C_EVENT        = 0 // CTRL+C signal
    | CTRL_BREAK_EVENT    = 1 // CTRL+BREAK signal
    | CTRL_CLOSE_EVENT    = 2 // Signal when the console window is closing
    | CTRL_LOGOFF_EVENT   = 5 // Signal when the user logs off ( received only by services )
    | CTRL_SHUTDOWN_EVENT = 6 // Signal when the system is shutting down (received only by services )

  type private ConsoleCtrlDelegate = delegate of CtrlTypes -> bool

  // Keep an instance of the delegate in a static scope
  let mutable private ctrlHandler: ConsoleCtrlDelegate = null

  let private consoleCtrlHandler func (ctrlType: CtrlTypes) =

    match ctrlType with

    // Returning false to allow the generic host to handle the Ctrl+C signal.
    | CtrlTypes.CTRL_C_EVENT
    | CtrlTypes.CTRL_BREAK_EVENT
      -> false

    | CtrlTypes.CTRL_CLOSE_EVENT
    | CtrlTypes.CTRL_LOGOFF_EVENT   (* received only by services *)
    | CtrlTypes.CTRL_SHUTDOWN_EVENT (* received only by services *)
    | _ ->

      int ctrlType
      |> fun n -> -n
      |> func
      |> Async.RunSynchronously

      true


  let private ctrlSignalsHandler (cancelfunc: int -> Async<unit>) =
    ctrlHandler <- ConsoleCtrlDelegate(fun ctrlType -> consoleCtrlHandler cancelfunc ctrlType )
    ctrlHandler

  [<DllImport("kernel32.dll")>]
  extern bool private SetConsoleCtrlHandler(ConsoleCtrlDelegate handler, bool add)

  let setCtrlSignalsHandler cancelfunc =
    SetConsoleCtrlHandler(ctrlSignalsHandler cancelfunc , true) |> ignore