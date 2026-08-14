using System.CommandLine;
using CodeGraphMcp.Commands;
using CodeGraphMcp.Config;

var sanitizedArgs = CommandLineSanitizer.SanitizeArgs(args);
var rootCommand   = CommandFactory.CreateRootCommand();

return await rootCommand.InvokeAsync(sanitizedArgs);
