using System.Text;
using TaskCli;

Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
var store = new TaskStore(Path.Combine(Directory.GetCurrentDirectory(), "tasks.json"));
var processor = new CommandProcessor(store, Console.Out, Console.Error);

return processor.Execute(args);
