# ConsoleWorkerfs

## Console App as a Background Worker

This console application is designed to support long-running tasks operating in the background. Developed using F# and targeting .NET 8, it provides an easy-to-manage and execute solution for specific background processes.

## How To Run 

To run the application, navigate to the project's root directory and execute the following command in your terminal:

```cmd
$ dotnet run
```

Ensure you have the .NET 8 SDK installed on your machine. This command compiles and runs the application directly from the source code, which is ideal for development and testing purposes.

## How To Build 

To build a self-contained executable suitable for deployment, run the following command:

```powershell
$ dotnet publish -c Release -r win-x64 --self-contained true -p:PublishDir=.\publish
```

This command creates a self-contained executable, reducing its size and making it easier to distribute. Before running this command, verify that your environment is set up with .NET 8 SDK.

## Creating a Shortcut for Windows Console Host

In Windows 11, where Windows Terminal is the default terminal application, creating a shortcut to launch the application via Windows Console Host can be particularly beneficial. This approach ensures compatibility and provides a more controlled environment for applications that may require specific console behaviors not supported by Windows Terminal.

To easily launch the application from anywhere using the Windows Console Host, create a shortcut with the following settings:

- **Link:** `%windir%\System32\conhost.exe cmd.exe /c .\publish\ConsoleWorkerfs.exe & pause`
- **Working Folder:** Leave this empty or specify the path to your application's working directory.

This shortcut method is especially useful for running the application in environments where the newer terminal features of Windows Terminal are not required or where the traditional console host's behavior is preferred. It offers a straightforward way to maintain the use of traditional console applications within the modern Windows 11 operating system.


## reference

[dfederm / GenericHostConsoleApp](https://github.com/dfederm/GenericHostConsoleApp)
