using System.CommandLine.Parsing;
using AiCodeGraph.Cli.Commands;

var rootCommand = CommandRegistry.Build();
var parseResult = CommandLineParser.Parse(rootCommand, args);
return await parseResult.InvokeAsync();
