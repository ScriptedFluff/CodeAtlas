using System.CommandLine;
using CodeGraphMcp.Commands;

var rootCommand = CommandFactory.CreateRootCommand();
return await rootCommand.InvokeAsync(args);
