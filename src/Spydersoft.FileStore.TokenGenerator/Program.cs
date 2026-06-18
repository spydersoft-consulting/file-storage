using Spydersoft.FileStore.TokenGenerator;

// First positional arg that isn't a flag is the key; flags start with "--".
var keyArg = Array.Find(args, a => !a.StartsWith("--", StringComparison.Ordinal));
var testKey = keyArg
    ?? Environment.GetEnvironmentVariable("FILESTORE_TEST_KEY")
    ?? "jRv3YFPH/19t9t5CgsEFgAkykfW5bQhHmceMprLgzlQ=";

var readOnly = args.Contains("--read-only");

Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(
    new { token = TokenGenerator.Generate(testKey, readOnly) }));
