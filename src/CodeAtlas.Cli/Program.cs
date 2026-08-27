using System.CommandLine;
using CodeAtlas.Commands;
using CodeAtlas.Core.Config;

var sanitizedArgs = CommandLineSanitizer.SanitizeArgs(args);
var rootCommand   = CommandFactory.CreateRootCommand();

return await rootCommand.InvokeAsync(sanitizedArgs);
