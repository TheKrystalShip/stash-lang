namespace Stash.Lsp.Analysis;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Single source of truth for all built-in functions, structs, namespaces,
/// keywords, and type names in the Stash language.
/// </summary>
public static class BuiltInRegistry
{
    // ── Data model ──

    public record BuiltInParam(string Name, string? Type = null);

    public record BuiltInFunction(string Name, BuiltInParam[] Parameters, string? ReturnType = null, string? Documentation = null)
    {
        public string Detail
        {
            get
            {
                var paramParts = Parameters.Select(p => p.Type != null ? $"{p.Name}: {p.Type}" : p.Name);
                var sig = $"fn {Name}({string.Join(", ", paramParts)})";
                return ReturnType != null ? $"{sig} -> {ReturnType}" : sig;
            }
        }

        public string[] ParamNames => Parameters.Select(p => p.Name).ToArray();
    }

    public record BuiltInField(string Name, string? Type);

    public record BuiltInStruct(string Name, BuiltInField[] Fields)
    {
        public string Detail
        {
            get
            {
                var fieldParts = Fields.Select(f => f.Type != null ? $"{f.Name}: {f.Type}" : f.Name);
                return $"struct {Name} {{ {string.Join(", ", fieldParts)} }}";
            }
        }
    }

    public record NamespaceFunction(string Namespace, string Name, BuiltInParam[] Parameters, string? ReturnType = null, bool IsVariadic = false, string? Documentation = null)
    {
        public string QualifiedName => $"{Namespace}.{Name}";

        public string Detail
        {
            get
            {
                var paramParts = Parameters.Select(p => p.Type != null ? $"{p.Name}: {p.Type}" : p.Name);
                var sig = $"fn {Namespace}.{Name}({string.Join(", ", paramParts)})";
                return ReturnType != null ? $"{sig} -> {ReturnType}" : sig;
            }
        }

        public string[] ParamNames => Parameters.Select(p => p.Name).ToArray();
    }

    public record NamespaceConstant(string Namespace, string Name, string Type, string Value, string? Documentation = null)
    {
        public string QualifiedName => $"{Namespace}.{Name}";
        public string Detail => $"const {Namespace}.{Name}: {Type} = {Value}";
    }

    // ── Built-in Structs ──

    public static readonly IReadOnlyList<BuiltInStruct> Structs = new[]
    {
        new BuiltInStruct("CommandResult", new BuiltInField[]
        {
            new("stdout", "string"),
            new("stderr", "string"),
            new("exitCode", "int"),
        }),
        new BuiltInStruct("Process", new BuiltInField[]
        {
            new("pid", "int"),
            new("command", "string"),
        }),
        new BuiltInStruct("HttpResponse", new BuiltInField[]
        {
            new("status", "int"),
            new("body", "string"),
            new("headers", "dict"),
        }),
    };

    // ── Built-in Global Functions ──

    public static readonly IReadOnlyList<BuiltInFunction> Functions = new[]
    {
        new BuiltInFunction("typeof", new[] { new BuiltInParam("value") }, "string",
            Documentation: "Returns the type name of a value as a string.\n@param value The value to inspect\n@return The type name: \"int\", \"float\", \"string\", \"bool\", \"null\", \"array\", \"dict\", \"struct\", \"function\", \"namespace\", \"range\", or \"enum\""),
        new BuiltInFunction("len", new[] { new BuiltInParam("value") }, "int",
            Documentation: "Returns the length of a string, array, or dictionary.\n@param value A string, array, or dictionary\n@return The number of characters, elements, or entries"),
        new BuiltInFunction("lastError", Array.Empty<BuiltInParam>(), "string",
            Documentation: "Returns the last runtime error message, or null if no error has occurred.\n@return The error message string, or null"),

        new BuiltInFunction("test", new[] { new BuiltInParam("name", "string"), new BuiltInParam("fn", "function") },
            Documentation: "Defines a test case with a name and a function body. The function is executed and the result is reported in TAP format.\n@param name The name of the test case\n@param fn The test function to execute"),
        new BuiltInFunction("skip", new[] { new BuiltInParam("name", "string"), new BuiltInParam("fn", "function") },
            Documentation: "Defines a test case that will be skipped. The test is recorded but not executed.\n@param name The name of the skipped test\n@param fn The test function (not executed)"),
        new BuiltInFunction("describe", new[] { new BuiltInParam("name", "string"), new BuiltInParam("fn", "function") },
            Documentation: "Groups related test cases into a named test suite. Tests defined inside the function body are logically grouped under this name.\n@param name The name of the test suite\n@param fn A function containing test() calls"),
        new BuiltInFunction("beforeAll", new[] { new BuiltInParam("fn", "function") },
            Documentation: "Registers a setup function that runs once before all tests in the current suite.\n@param fn The setup function to execute"),
        new BuiltInFunction("afterAll", new[] { new BuiltInParam("fn", "function") },
            Documentation: "Registers a teardown function that runs once after all tests in the current suite.\n@param fn The teardown function to execute"),
        new BuiltInFunction("beforeEach", new[] { new BuiltInParam("fn", "function") },
            Documentation: "Registers a setup function that runs before each test in the current suite.\n@param fn The setup function to execute before each test"),
        new BuiltInFunction("afterEach", new[] { new BuiltInParam("fn", "function") },
            Documentation: "Registers a teardown function that runs after each test in the current suite.\n@param fn The teardown function to execute after each test"),
        new BuiltInFunction("captureOutput", new[] { new BuiltInParam("fn", "function") }, "string",
            Documentation: "Executes a function while capturing all standard output. Returns the captured output as a string.\n@param fn The function whose output to capture\n@return The captured stdout output as a string"),
        new BuiltInFunction("range", new[] { new BuiltInParam("start_or_end", "int"), new BuiltInParam("end", "int"), new BuiltInParam("step", "int") }, "array",
            Documentation: "Creates an array of integers in sequence. With one argument, generates 0 to end (exclusive). With two, generates start to end (exclusive). With three, uses the given step.\n@param start_or_end The end value (if one arg) or start value (if two/three args)\n@param end The end value (exclusive)\n@param step The increment between values\n@return An array of integers"),
        new BuiltInFunction("exit", new[] { new BuiltInParam("code", "int") },
            Documentation: "Terminates the program immediately with the specified exit code.\n@param code The exit code to return to the operating system"),
        new BuiltInFunction("hash", new[] { new BuiltInParam("value") }, "int",
            Documentation: "Returns a hash code for the given value.\n@param value The value to hash\n@return An integer hash code"),
    };

    // ── Built-in Namespace Functions ──

    public static readonly IReadOnlyList<NamespaceFunction> NamespaceFunctions = new[]
    {
        // io namespace
        new NamespaceFunction("io", "println", new[] { new BuiltInParam("value") }, IsVariadic: true,
            Documentation: "Prints a value followed by a newline to standard output. When called with no arguments, prints an empty line.\n@param value The value to print (converted to string). Optional — defaults to empty string"),
        new NamespaceFunction("io", "print", new[] { new BuiltInParam("value") },
            Documentation: "Prints a value to standard output without a trailing newline.\n@param value The value to print (converted to string)"),
        new NamespaceFunction("io", "readLine", new[] { new BuiltInParam("prompt", "string") }, "string", IsVariadic: true,
            Documentation: "Reads a line of input from standard input. Optionally displays a prompt.\n@param prompt An optional prompt string to display before reading\n@return The line of input as a string"),
        new NamespaceFunction("io", "eprintln", new[] { new BuiltInParam("value") },
            Documentation: "Prints a value followed by a newline to standard error.\n@param value The value to print (converted to string)"),
        new NamespaceFunction("io", "eprint", new[] { new BuiltInParam("value") },
            Documentation: "Prints a value to standard error without a trailing newline.\n@param value The value to print (converted to string)"),
        // conv namespace
        new NamespaceFunction("conv", "toStr", new[] { new BuiltInParam("value") }, "string",
            Documentation: "Converts any value to its string representation.\n@param value The value to convert\n@return The string representation of the value"),
        new NamespaceFunction("conv", "toInt", new[] { new BuiltInParam("value") }, "int",
            Documentation: "Parses a string or converts a number to an integer. Floats are truncated. Returns null on failure.\n@param value A string or number to convert\n@return The integer value, or null if parsing fails"),
        new NamespaceFunction("conv", "toFloat", new[] { new BuiltInParam("value") }, "float",
            Documentation: "Parses a string or converts a number to a float. Returns null on failure.\n@param value A string or number to convert\n@return The float value, or null if parsing fails"),
        new NamespaceFunction("conv", "toBool", new[] { new BuiltInParam("value") }, "bool",
            Documentation: "Converts a value to boolean using truthiness rules. false, null, 0, 0.0, and \"\" are falsy; everything else is truthy.\n@param value The value to convert\n@return The boolean result"),
        new NamespaceFunction("conv", "toHex", new[] { new BuiltInParam("n", "int") }, "string",
            Documentation: "Converts an integer to its hexadecimal string representation.\n@param n The integer to convert\n@return The hexadecimal string (e.g., \"ff\")"),
        new NamespaceFunction("conv", "toOct", new[] { new BuiltInParam("n", "int") }, "string",
            Documentation: "Converts an integer to its octal string representation.\n@param n The integer to convert\n@return The octal string"),
        new NamespaceFunction("conv", "toBin", new[] { new BuiltInParam("n", "int") }, "string",
            Documentation: "Converts an integer to its binary string representation.\n@param n The integer to convert\n@return The binary string"),
        new NamespaceFunction("conv", "fromHex", new[] { new BuiltInParam("s", "string") }, "int",
            Documentation: "Parses a hexadecimal string to an integer. Supports optional \"0x\" prefix.\n@param s The hexadecimal string to parse\n@return The parsed integer value"),
        new NamespaceFunction("conv", "fromOct", new[] { new BuiltInParam("s", "string") }, "int",
            Documentation: "Parses an octal string to an integer. Supports optional \"0o\" prefix.\n@param s The octal string to parse\n@return The parsed integer value"),
        new NamespaceFunction("conv", "fromBin", new[] { new BuiltInParam("s", "string") }, "int",
            Documentation: "Parses a binary string to an integer. Supports optional \"0b\" prefix.\n@param s The binary string to parse\n@return The parsed integer value"),
        new NamespaceFunction("conv", "charCode", new[] { new BuiltInParam("s", "string") }, "int",
            Documentation: "Returns the Unicode code point of the first character in the string.\n@param s A non-empty string\n@return The Unicode code point as an integer"),
        new NamespaceFunction("conv", "fromCharCode", new[] { new BuiltInParam("n", "int") }, "string",
            Documentation: "Returns a single-character string from a Unicode code point.\n@param n The Unicode code point\n@return A string containing the character"),
        // env namespace
        new NamespaceFunction("env", "get", new[] { new BuiltInParam("name", "string") }, "string",
            Documentation: "Reads the value of an environment variable.\n@param name The name of the environment variable\n@return The value as a string, or null if not set"),
        new NamespaceFunction("env", "set", new[] { new BuiltInParam("name", "string"), new BuiltInParam("value", "string") },
            Documentation: "Sets an environment variable for the current process.\n@param name The name of the environment variable\n@param value The value to set"),
        new NamespaceFunction("env", "has", new[] { new BuiltInParam("name", "string") }, "bool",
            Documentation: "Checks whether an environment variable is set.\n@param name The name of the environment variable\n@return true if the variable exists, false otherwise"),
        new NamespaceFunction("env", "all", Array.Empty<BuiltInParam>(), "dict",
            Documentation: "Returns all environment variables as a dictionary.\n@return A dictionary of all environment variable name-value pairs"),
        new NamespaceFunction("env", "withPrefix", new[] { new BuiltInParam("prefix", "string") }, "dict",
            Documentation: "Returns all environment variables whose names start with the given prefix.\n@param prefix The prefix to filter by\n@return A dictionary of matching environment variable name-value pairs"),
        new NamespaceFunction("env", "remove", new[] { new BuiltInParam("name", "string") },
            Documentation: "Deletes an environment variable from the current process.\n@param name The name of the environment variable to remove"),
        new NamespaceFunction("env", "cwd", Array.Empty<BuiltInParam>(), "string",
            Documentation: "Returns the current working directory path.\n@return The absolute path of the current working directory"),
        new NamespaceFunction("env", "home", Array.Empty<BuiltInParam>(), "string",
            Documentation: "Returns the user's home directory path.\n@return The absolute path of the home directory"),
        new NamespaceFunction("env", "hostname", Array.Empty<BuiltInParam>(), "string",
            Documentation: "Returns the machine's hostname.\n@return The hostname as a string"),
        new NamespaceFunction("env", "user", Array.Empty<BuiltInParam>(), "string",
            Documentation: "Returns the current username.\n@return The username as a string"),
        new NamespaceFunction("env", "os", Array.Empty<BuiltInParam>(), "string",
            Documentation: "Returns the operating system name.\n@return \"linux\", \"macos\", or \"windows\""),
        new NamespaceFunction("env", "arch", Array.Empty<BuiltInParam>(), "string",
            Documentation: "Returns the CPU architecture.\n@return The architecture string (e.g., \"x64\", \"arm64\")"),
        new NamespaceFunction("env", "loadFile", new[] { new BuiltInParam("path", "string"), new BuiltInParam("prefix", "string") }, "int", IsVariadic: true,
            Documentation: "Loads environment variables from a .env-style file. When a prefix is provided, each key is prepended with it before being set.\n@param path The path to the .env file\n@param prefix Optional prefix to prepend to each variable name\n@return The number of variables loaded"),
        new NamespaceFunction("env", "saveFile", new[] { new BuiltInParam("path", "string") },
            Documentation: "Saves all current environment variables to a .env-style file.\n@param path The path to write the .env file"),
        // process namespace
        new NamespaceFunction("process", "exit", new[] { new BuiltInParam("code", "int") },
            Documentation: "Terminates the current process with the specified exit code.\n@param code The exit code to return to the operating system"),
        new NamespaceFunction("process", "exec", new[] { new BuiltInParam("cmd", "string") },
            Documentation: "Executes a shell command synchronously and waits for it to complete. Replaces the current process.\n@param cmd The shell command to execute"),
        new NamespaceFunction("process", "spawn", new[] { new BuiltInParam("cmd", "string") }, "Process",
            Documentation: "Starts a new process asynchronously without waiting for it to complete. Returns a Process handle for monitoring and control.\n@param cmd The shell command to execute\n@return A Process struct with pid and command fields"),
        new NamespaceFunction("process", "wait", new[] { new BuiltInParam("proc", "Process") }, "CommandResult",
            Documentation: "Waits for a spawned process to complete and returns the result.\n@param proc The Process handle returned by process.spawn()\n@return A CommandResult with stdout, stderr, and exitCode fields"),
        new NamespaceFunction("process", "waitTimeout", new[] { new BuiltInParam("proc", "Process"), new BuiltInParam("ms", "int") }, "CommandResult",
            Documentation: "Waits for a spawned process to complete with a timeout in milliseconds. Returns null if the process does not finish in time.\n@param proc The Process handle returned by process.spawn()\n@param ms Maximum time to wait in milliseconds\n@return A CommandResult with stdout, stderr, and exitCode, or null on timeout"),
        new NamespaceFunction("process", "kill", new[] { new BuiltInParam("proc", "Process") }, "bool",
            Documentation: "Forcefully terminates a spawned process.\n@param proc The Process handle to kill\n@return true if the process was successfully killed"),
        new NamespaceFunction("process", "isAlive", new[] { new BuiltInParam("proc", "Process") }, "bool",
            Documentation: "Checks whether a spawned process is still running.\n@param proc The Process handle to check\n@return true if the process is still running"),
        new NamespaceFunction("process", "pid", new[] { new BuiltInParam("proc", "Process") }, "int",
            Documentation: "Returns the operating system process ID of a spawned process.\n@param proc The Process handle\n@return The process ID as an integer"),
        new NamespaceFunction("process", "signal", new[] { new BuiltInParam("proc", "Process"), new BuiltInParam("sig", "int") }, "bool",
            Documentation: "Sends a signal to a spawned process. Use constants like process.SIGTERM, process.SIGKILL, etc.\n@param proc The Process handle to signal\n@param sig The signal number to send\n@return true if the signal was sent successfully"),
        new NamespaceFunction("process", "detach", new[] { new BuiltInParam("proc", "Process") }, "bool",
            Documentation: "Detaches a spawned process so it continues running independently after the parent exits.\n@param proc The Process handle to detach\n@return true if the process was successfully detached"),
        new NamespaceFunction("process", "list", Array.Empty<BuiltInParam>(), "array",
            Documentation: "Returns an array of all currently tracked spawned processes.\n@return An array of Process structs"),
        new NamespaceFunction("process", "read", new[] { new BuiltInParam("proc", "Process") }, "string",
            Documentation: "Reads available stdout output from a running spawned process.\n@param proc The Process handle to read from\n@return The available output as a string"),
        new NamespaceFunction("process", "write", new[] { new BuiltInParam("proc", "Process"), new BuiltInParam("data", "string") }, "bool",
            Documentation: "Writes data to the stdin of a running spawned process.\n@param proc The Process handle to write to\n@param data The string data to write\n@return true if the write was successful"),
        new NamespaceFunction("process", "onExit", new[] { new BuiltInParam("proc", "Process"), new BuiltInParam("callback", "function") },
            Documentation: "Registers a callback function to be called when a spawned process exits. The callback receives the CommandResult as its argument.\n@param proc The Process handle to monitor\n@param callback A function that accepts one argument (the CommandResult)"),
        new NamespaceFunction("process", "daemonize", new[] { new BuiltInParam("cmd", "string") }, "Process",
            Documentation: "Launches a command as a daemon process that is not tracked and survives script exit. Returns a Process handle with pid and command fields.\n@param cmd The command to launch as a daemon\n@return A Process struct (not tracked for cleanup)"),
        new NamespaceFunction("process", "find", new[] { new BuiltInParam("name", "string") }, "array",
            Documentation: "Finds system processes by name. Returns an array of Process handles with pid and command fields.\n@param name The process name to search for\n@return An array of Process structs matching the name"),
        new NamespaceFunction("process", "exists", new[] { new BuiltInParam("pid", "int") }, "bool",
            Documentation: "Checks if a system process exists and is running by its PID.\n@param pid The process ID to check\n@return true if a process with the given PID exists and is running"),
        new NamespaceFunction("process", "waitAll", new[] { new BuiltInParam("procs", "array") }, "array",
            Documentation: "Waits for all processes in an array to exit. Returns an array of CommandResult objects in the same order.\n@param procs An array of Process handles to wait for\n@return An array of CommandResult structs with stdout, stderr, and exitCode"),
        new NamespaceFunction("process", "waitAny", new[] { new BuiltInParam("procs", "array") }, "CommandResult",
            Documentation: "Waits for the first process in an array to exit and returns its result.\n@param procs An array of Process handles to wait for\n@return The CommandResult of the first process to exit"),
        new NamespaceFunction("process", "chdir", new[] { new BuiltInParam("path", "string") }, "null",
            Documentation: "Changes the current working directory of the process.\n@param path The directory path to change to (absolute or relative)\n@return null"),
        new NamespaceFunction("process", "withDir", new[] { new BuiltInParam("path", "string"), new BuiltInParam("fn", "function") }, "any",
            Documentation: "Runs a function with the working directory temporarily changed to the given path. The original directory is restored when the function returns, even if it throws an error.\n@param path The directory to change to\n@param fn A function to execute in the changed directory\n@return The return value of fn"),
        // file system namespace
        new NamespaceFunction("fs", "readFile", new[] { new BuiltInParam("path", "string") }, "string",
            Documentation: "Reads the entire contents of a file as a string.\n@param path The path to the file\n@return The file contents as a string"),
        new NamespaceFunction("fs", "writeFile", new[] { new BuiltInParam("path", "string"), new BuiltInParam("content", "string") },
            Documentation: "Writes a string to a file, creating or overwriting it.\n@param path The path to the file\n@param content The string content to write"),
        new NamespaceFunction("fs", "exists", new[] { new BuiltInParam("path", "string") }, "bool",
            Documentation: "Checks whether a file exists at the given path.\n@param path The path to check\n@return true if a file exists at the path"),
        new NamespaceFunction("fs", "dirExists", new[] { new BuiltInParam("path", "string") }, "bool",
            Documentation: "Checks whether a directory exists at the given path.\n@param path The path to check\n@return true if a directory exists at the path"),
        new NamespaceFunction("fs", "pathExists", new[] { new BuiltInParam("path", "string") }, "bool",
            Documentation: "Checks whether a file or directory exists at the given path.\n@param path The path to check\n@return true if a file or directory exists at the path"),
        new NamespaceFunction("fs", "createDir", new[] { new BuiltInParam("path", "string") },
            Documentation: "Creates a directory, including any necessary parent directories.\n@param path The path of the directory to create"),
        new NamespaceFunction("fs", "delete", new[] { new BuiltInParam("path", "string") },
            Documentation: "Deletes a file or directory. Directories are deleted recursively.\n@param path The path to the file or directory to delete"),
        new NamespaceFunction("fs", "copy", new[] { new BuiltInParam("src", "string"), new BuiltInParam("dst", "string") },
            Documentation: "Copies a file from source to destination, overwriting if it exists.\n@param src The source file path\n@param dst The destination file path"),
        new NamespaceFunction("fs", "move", new[] { new BuiltInParam("src", "string"), new BuiltInParam("dst", "string") },
            Documentation: "Moves or renames a file, overwriting the destination if it exists.\n@param src The source file path\n@param dst The destination file path"),
        new NamespaceFunction("fs", "size", new[] { new BuiltInParam("path", "string") }, "int",
            Documentation: "Returns the size of a file in bytes.\n@param path The path to the file\n@return The file size in bytes"),
        new NamespaceFunction("fs", "listDir", new[] { new BuiltInParam("path", "string") }, "array",
            Documentation: "Lists the entries (files and directories) in a directory.\n@param path The path to the directory\n@return An array of entry names"),
        new NamespaceFunction("fs", "appendFile", new[] { new BuiltInParam("path", "string"), new BuiltInParam("content", "string") },
            Documentation: "Appends a string to the end of a file. Creates the file if it does not exist.\n@param path The path to the file\n@param content The string content to append"),
        new NamespaceFunction("fs", "readLines", new[] { new BuiltInParam("path", "string") }, "array",
            Documentation: "Reads a file and returns its contents as an array of lines.\n@param path The path to the file\n@return An array of strings, one per line"),
        new NamespaceFunction("fs", "glob", new[] { new BuiltInParam("pattern", "string") }, "array",
            Documentation: "Finds files matching a glob pattern (e.g., \"*.txt\", \"src/**/*.stash\").\n@param pattern The glob pattern to match\n@return An array of matching file paths"),
        new NamespaceFunction("fs", "isFile", new[] { new BuiltInParam("path", "string") }, "bool",
            Documentation: "Checks whether the path points to a regular file.\n@param path The path to check\n@return true if the path is a regular file"),
        new NamespaceFunction("fs", "isDir", new[] { new BuiltInParam("path", "string") }, "bool",
            Documentation: "Checks whether the path points to a directory.\n@param path The path to check\n@return true if the path is a directory"),
        new NamespaceFunction("fs", "isSymlink", new[] { new BuiltInParam("path", "string") }, "bool",
            Documentation: "Checks whether the path points to a symbolic link.\n@param path The path to check\n@return true if the path is a symbolic link"),
        new NamespaceFunction("fs", "tempFile", Array.Empty<BuiltInParam>(), "string",
            Documentation: "Creates a temporary file and returns its path.\n@return The absolute path to the created temporary file"),
        new NamespaceFunction("fs", "tempDir", Array.Empty<BuiltInParam>(), "string",
            Documentation: "Creates a temporary directory and returns its path.\n@return The absolute path to the created temporary directory"),
        new NamespaceFunction("fs", "modifiedAt", new[] { new BuiltInParam("path", "string") }, "float",
            Documentation: "Returns the last modification time of a file as a Unix timestamp.\n@param path The path to the file\n@return The last modified time as a Unix timestamp (seconds since epoch)"),
        new NamespaceFunction("fs", "walk", new[] { new BuiltInParam("path", "string") }, "array",
            Documentation: "Recursively lists all files under a directory.\n@param path The root directory path to walk\n@return An array of all file paths found recursively"),
        new NamespaceFunction("fs", "readable", new[] { new BuiltInParam("path", "string") }, "bool",
            Documentation: "Checks whether the current user has read permission on a file.\n@param path The path to the file\n@return true if the file is readable"),
        new NamespaceFunction("fs", "writable", new[] { new BuiltInParam("path", "string") }, "bool",
            Documentation: "Checks whether the current user has write permission on a file.\n@param path The path to the file\n@return true if the file is writable"),
        new NamespaceFunction("fs", "executable", new[] { new BuiltInParam("path", "string") }, "bool",
            Documentation: "Checks whether the current user has execute permission on a file.\n@param path The path to the file\n@return true if the file is executable"),
        new NamespaceFunction("fs", "createFile", new[] { new BuiltInParam("path", "string") },
            Documentation: "Creates an empty file or updates the last-modified timestamp of an existing file.\n@param path The path to the file"),
        new NamespaceFunction("fs", "symlink", new[] { new BuiltInParam("target", "string"), new BuiltInParam("path", "string") },
            Documentation: "Creates a symbolic link at path pointing to target.\n@param target The path the symlink will point to\n@param path The path where the symlink will be created"),
        new NamespaceFunction("fs", "stat", new[] { new BuiltInParam("path", "string") }, "dict",
            Documentation: "Returns a dictionary with file metadata: size, isFile, isDir, isSymlink, modified, created, name.\n@param path The path to inspect\n@return A dictionary with file information"),
        // path namespace
        new NamespaceFunction("path", "abs", new[] { new BuiltInParam("path", "string") }, "string",
            Documentation: "Converts a relative path to an absolute path.\n@param path The path to resolve\n@return The absolute path"),
        new NamespaceFunction("path", "dir", new[] { new BuiltInParam("path", "string") }, "string",
            Documentation: "Returns the directory portion of a path.\n@param path The file path\n@return The directory path (without the filename)"),
        new NamespaceFunction("path", "base", new[] { new BuiltInParam("path", "string") }, "string",
            Documentation: "Returns the filename with extension from a path.\n@param path The file path\n@return The filename including extension (e.g., \"file.txt\")"),
        new NamespaceFunction("path", "ext", new[] { new BuiltInParam("path", "string") }, "string",
            Documentation: "Returns the file extension from a path, including the leading dot.\n@param path The file path\n@return The file extension (e.g., \".txt\")"),
        new NamespaceFunction("path", "join", new[] { new BuiltInParam("a", "string"), new BuiltInParam("b", "string") }, "string",
            Documentation: "Joins two path segments with the appropriate separator.\n@param a The first path segment\n@param b The second path segment\n@return The combined path"),
        new NamespaceFunction("path", "name", new[] { new BuiltInParam("path", "string") }, "string",
            Documentation: "Returns the filename without extension from a path.\n@param path The file path\n@return The filename without extension (e.g., \"file\")"),
        // arr namespace
        new NamespaceFunction("arr", "push", new[] { new BuiltInParam("array", "array"), new BuiltInParam("value") },
            Documentation: "Adds a value to the end of an array. Mutates the array in-place.\n@param array The array to modify\n@param value The value to add"),
        new NamespaceFunction("arr", "pop", new[] { new BuiltInParam("array", "array") },
            Documentation: "Removes and returns the last element of an array. Throws an error if the array is empty.\n@param array The array to pop from\n@return The removed element"),
        new NamespaceFunction("arr", "peek", new[] { new BuiltInParam("array", "array") },
            Documentation: "Returns the last element of an array without removing it. Throws an error if the array is empty.\n@param array The array to peek at\n@return The last element"),
        new NamespaceFunction("arr", "insert", new[] { new BuiltInParam("array", "array"), new BuiltInParam("index", "int"), new BuiltInParam("value") },
            Documentation: "Inserts a value at the specified index, shifting subsequent elements to the right. Mutates the array in-place.\n@param array The array to modify\n@param index The position at which to insert\n@param value The value to insert"),
        new NamespaceFunction("arr", "removeAt", new[] { new BuiltInParam("array", "array"), new BuiltInParam("index", "int") },
            Documentation: "Removes and returns the element at the specified index. Shifts subsequent elements to the left.\n@param array The array to modify\n@param index The index of the element to remove\n@return The removed element"),
        new NamespaceFunction("arr", "remove", new[] { new BuiltInParam("array", "array"), new BuiltInParam("value") }, "bool",
            Documentation: "Removes the first occurrence of a value from an array.\n@param array The array to modify\n@param value The value to remove\n@return true if the value was found and removed, false otherwise"),
        new NamespaceFunction("arr", "clear", new[] { new BuiltInParam("array", "array") },
            Documentation: "Removes all elements from an array. Mutates the array in-place.\n@param array The array to clear"),
        new NamespaceFunction("arr", "contains", new[] { new BuiltInParam("array", "array"), new BuiltInParam("value") }, "bool",
            Documentation: "Checks whether an array contains a specific value.\n@param array The array to search\n@param value The value to look for\n@return true if the value is found in the array"),
        new NamespaceFunction("arr", "indexOf", new[] { new BuiltInParam("array", "array"), new BuiltInParam("value") }, "int",
            Documentation: "Returns the index of the first occurrence of a value in an array.\n@param array The array to search\n@param value The value to look for\n@return The zero-based index, or -1 if not found"),
        new NamespaceFunction("arr", "slice", new[] { new BuiltInParam("array", "array"), new BuiltInParam("start", "int"), new BuiltInParam("end", "int") }, "array", IsVariadic: true,
            Documentation: "Returns a new sub-array from start (inclusive) to end (exclusive). If end is omitted, slices to the end of the array.\n@param array The source array\n@param start The start index (inclusive)\n@param end The end index (exclusive, optional)\n@return A new array containing the sliced elements"),
        new NamespaceFunction("arr", "concat", new[] { new BuiltInParam("array1", "array"), new BuiltInParam("array2", "array") }, "array",
            Documentation: "Returns a new array combining two arrays. Does not modify the original arrays.\n@param array1 The first array\n@param array2 The second array\n@return A new array containing all elements from both arrays"),
        new NamespaceFunction("arr", "join", new[] { new BuiltInParam("array", "array"), new BuiltInParam("separator", "string") }, "string",
            Documentation: "Joins all array elements into a single string with a separator between each element.\n@param array The array to join\n@param separator The string to place between elements\n@return The joined string"),
        new NamespaceFunction("arr", "reverse", new[] { new BuiltInParam("array", "array") },
            Documentation: "Reverses the order of elements in an array. Mutates the array in-place.\n@param array The array to reverse"),
        new NamespaceFunction("arr", "sort", new[] { new BuiltInParam("array", "array") },
            Documentation: "Sorts an array in ascending order. Mutates the array in-place. Works with numbers and strings; throws an error on mixed types.\n@param array The array to sort"),
        new NamespaceFunction("arr", "map", new[] { new BuiltInParam("array", "array"), new BuiltInParam("fn", "function") }, "array",
            Documentation: "Returns a new array with the results of calling a function on every element.\n@param array The source array\n@param fn A function that takes an element and returns a transformed value\n@return A new array of transformed elements"),
        new NamespaceFunction("arr", "filter", new[] { new BuiltInParam("array", "array"), new BuiltInParam("fn", "function") }, "array",
            Documentation: "Returns a new array containing only elements for which the predicate function returns a truthy value.\n@param array The source array\n@param fn A predicate function that takes an element and returns a truthy or falsy value\n@return A new array of matching elements"),
        new NamespaceFunction("arr", "forEach", new[] { new BuiltInParam("array", "array"), new BuiltInParam("fn", "function") },
            Documentation: "Calls a function for each element in the array.\n@param array The array to iterate\n@param fn A function to call with each element"),
        new NamespaceFunction("arr", "find", new[] { new BuiltInParam("array", "array"), new BuiltInParam("fn", "function") },
            Documentation: "Returns the first element for which the predicate function returns a truthy value, or null if none match.\n@param array The array to search\n@param fn A predicate function that takes an element\n@return The first matching element, or null"),
        new NamespaceFunction("arr", "reduce", new[] { new BuiltInParam("array", "array"), new BuiltInParam("fn", "function"), new BuiltInParam("initial") },
            Documentation: "Reduces an array to a single value by applying a function to an accumulator and each element.\n@param array The array to reduce\n@param fn A function taking (accumulator, element) and returning the new accumulator\n@param initial The initial value of the accumulator\n@return The final accumulator value"),
        new NamespaceFunction("arr", "unique", new[] { new BuiltInParam("array", "array") }, "array",
            Documentation: "Returns a new array with duplicate values removed, preserving the order of first occurrences.\n@param array The array to deduplicate\n@return A new array with unique elements"),
        new NamespaceFunction("arr", "any", new[] { new BuiltInParam("array", "array"), new BuiltInParam("fn", "function") }, "bool",
            Documentation: "Returns true if at least one element satisfies the predicate function.\n@param array The array to test\n@param fn A predicate function that takes an element\n@return true if any element passes the test"),
        new NamespaceFunction("arr", "every", new[] { new BuiltInParam("array", "array"), new BuiltInParam("fn", "function") }, "bool",
            Documentation: "Returns true if all elements satisfy the predicate function.\n@param array The array to test\n@param fn A predicate function that takes an element\n@return true if every element passes the test"),
        new NamespaceFunction("arr", "flat", new[] { new BuiltInParam("array", "array") }, "array",
            Documentation: "Flattens one level of nesting. Inner arrays are expanded into the result; non-array elements are kept as-is.\n@param array The array to flatten\n@return A new flattened array"),
        new NamespaceFunction("arr", "flatMap", new[] { new BuiltInParam("array", "array"), new BuiltInParam("fn", "function") }, "array",
            Documentation: "Maps each element through a function, then flattens the result by one level.\n@param array The source array\n@param fn A function that takes an element and returns a value or array\n@return A new array with mapped and flattened results"),
        new NamespaceFunction("arr", "findIndex", new[] { new BuiltInParam("array", "array"), new BuiltInParam("fn", "function") }, "int",
            Documentation: "Returns the index of the first element that satisfies the predicate, or -1 if none match.\n@param array The array to search\n@param fn A predicate function that takes an element\n@return The zero-based index, or -1 if not found"),
        new NamespaceFunction("arr", "count", new[] { new BuiltInParam("array", "array"), new BuiltInParam("fn", "function") }, "int",
            Documentation: "Counts the number of elements that satisfy the predicate function.\n@param array The array to count in\n@param fn A predicate function that takes an element\n@return The count of matching elements"),
        new NamespaceFunction("arr", "sortBy", new[] { new BuiltInParam("array", "array"), new BuiltInParam("fn", "function") }, "array",
            Documentation: "Returns a new array sorted by keys extracted via a function. The original array is not modified.\n@param array The source array\n@param fn A function that takes an element and returns a comparable sort key\n@return A new sorted array"),
        new NamespaceFunction("arr", "groupBy", new[] { new BuiltInParam("array", "array"), new BuiltInParam("fn", "function") }, "dict",
            Documentation: "Groups array elements into a dictionary keyed by the result of calling a function on each element.\n@param array The source array\n@param fn A function that takes an element and returns a grouping key\n@return A dictionary mapping keys to arrays of matching elements"),
        new NamespaceFunction("arr", "sum", new[] { new BuiltInParam("array", "array") }, "number",
            Documentation: "Returns the sum of all numeric elements in the array.\n@param array An array of numbers\n@return The sum as an int (if all elements are ints) or float"),
        new NamespaceFunction("arr", "min", new[] { new BuiltInParam("array", "array") }, "number",
            Documentation: "Returns the minimum numeric element in the array.\n@param array A non-empty array of numbers\n@return The smallest element"),
        new NamespaceFunction("arr", "max", new[] { new BuiltInParam("array", "array") }, "number",
            Documentation: "Returns the maximum numeric element in the array.\n@param array A non-empty array of numbers\n@return The largest element"),
        // dict namespace
        new NamespaceFunction("dict", "new", Array.Empty<BuiltInParam>(), "dict",
            Documentation: "Creates a new empty dictionary.\n@return An empty dictionary"),
        new NamespaceFunction("dict", "get", new[] { new BuiltInParam("dict", "dict"), new BuiltInParam("key") },
            Documentation: "Returns the value associated with a key, or null if the key does not exist.\n@param dict The dictionary to look up\n@param key The key to search for\n@return The associated value, or null"),
        new NamespaceFunction("dict", "set", new[] { new BuiltInParam("dict", "dict"), new BuiltInParam("key"), new BuiltInParam("value") },
            Documentation: "Sets a key-value pair in the dictionary. Overwrites the value if the key already exists. Mutates the dictionary.\n@param dict The dictionary to modify\n@param key The key to set\n@param value The value to associate with the key"),
        new NamespaceFunction("dict", "has", new[] { new BuiltInParam("dict", "dict"), new BuiltInParam("key") }, "bool",
            Documentation: "Checks whether a key exists in the dictionary.\n@param dict The dictionary to check\n@param key The key to search for\n@return true if the key exists"),
        new NamespaceFunction("dict", "remove", new[] { new BuiltInParam("dict", "dict"), new BuiltInParam("key") }, "bool",
            Documentation: "Removes a key and its value from the dictionary.\n@param dict The dictionary to modify\n@param key The key to remove\n@return true if the key was found and removed"),
        new NamespaceFunction("dict", "clear", new[] { new BuiltInParam("dict", "dict") },
            Documentation: "Removes all entries from the dictionary. Mutates the dictionary in-place.\n@param dict The dictionary to clear"),
        new NamespaceFunction("dict", "keys", new[] { new BuiltInParam("dict", "dict") }, "array",
            Documentation: "Returns an array of all keys in the dictionary.\n@param dict The dictionary\n@return An array of keys"),
        new NamespaceFunction("dict", "values", new[] { new BuiltInParam("dict", "dict") }, "array",
            Documentation: "Returns an array of all values in the dictionary.\n@param dict The dictionary\n@return An array of values"),
        new NamespaceFunction("dict", "size", new[] { new BuiltInParam("dict", "dict") }, "int",
            Documentation: "Returns the number of key-value pairs in the dictionary.\n@param dict The dictionary\n@return The number of entries"),
        new NamespaceFunction("dict", "pairs", new[] { new BuiltInParam("dict", "dict") }, "array",
            Documentation: "Returns an array of Pair structs, each with .key and .value fields.\n@param dict The dictionary\n@return An array of Pair structs"),
        new NamespaceFunction("dict", "forEach", new[] { new BuiltInParam("dict", "dict"), new BuiltInParam("fn", "function") },
            Documentation: "Calls a function for each key-value pair in the dictionary.\n@param dict The dictionary to iterate\n@param fn A function taking (key, value) for each entry"),
        new NamespaceFunction("dict", "merge", new[] { new BuiltInParam("dict1", "dict"), new BuiltInParam("dict2", "dict") }, "dict",
            Documentation: "Returns a new dictionary combining both dictionaries. When keys conflict, values from the second dictionary take priority.\n@param dict1 The base dictionary\n@param dict2 The dictionary whose values take priority\n@return A new merged dictionary"),
        new NamespaceFunction("dict", "map", new[] { new BuiltInParam("dict", "dict"), new BuiltInParam("fn", "function") }, "dict",
            Documentation: "Returns a new dictionary with values transformed by a function. Keys are preserved.\n@param dict The dictionary to map over\n@param fn A function taking (key, value) that returns the new value\n@return A new dictionary with mapped values"),
        new NamespaceFunction("dict", "filter", new[] { new BuiltInParam("dict", "dict"), new BuiltInParam("fn", "function") }, "dict",
            Documentation: "Returns a new dictionary containing only entries for which the function returns a truthy value.\n@param dict The dictionary to filter\n@param fn A function taking (key, value) that returns a truthy or falsy value\n@return A new dictionary with filtered entries"),
        // str namespace
        new NamespaceFunction("str", "upper", new[] { new BuiltInParam("s", "string") }, "string",
            Documentation: "Converts all characters in a string to uppercase.\n@param s The input string\n@return A new string with all characters in uppercase"),
        new NamespaceFunction("str", "lower", new[] { new BuiltInParam("s", "string") }, "string",
            Documentation: "Converts all characters in a string to lowercase.\n@param s The input string\n@return A new string with all characters in lowercase"),
        new NamespaceFunction("str", "trim", new[] { new BuiltInParam("s", "string") }, "string",
            Documentation: "Removes leading and trailing whitespace from a string.\n@param s The input string\n@return A new string with whitespace removed from both ends"),
        new NamespaceFunction("str", "trimStart", new[] { new BuiltInParam("s", "string") }, "string",
            Documentation: "Removes leading whitespace from a string.\n@param s The input string\n@return A new string with leading whitespace removed"),
        new NamespaceFunction("str", "trimEnd", new[] { new BuiltInParam("s", "string") }, "string",
            Documentation: "Removes trailing whitespace from a string.\n@param s The input string\n@return A new string with trailing whitespace removed"),
        new NamespaceFunction("str", "contains", new[] { new BuiltInParam("s", "string"), new BuiltInParam("substring", "string") }, "bool",
            Documentation: "Checks whether a string contains a substring.\n@param s The string to search in\n@param substring The substring to find\n@return true if the substring is found"),
        new NamespaceFunction("str", "startsWith", new[] { new BuiltInParam("s", "string"), new BuiltInParam("prefix", "string") }, "bool",
            Documentation: "Checks whether a string starts with a given prefix.\n@param s The string to check\n@param prefix The prefix to look for\n@return true if the string starts with the prefix"),
        new NamespaceFunction("str", "endsWith", new[] { new BuiltInParam("s", "string"), new BuiltInParam("suffix", "string") }, "bool",
            Documentation: "Checks whether a string ends with a given suffix.\n@param s The string to check\n@param suffix The suffix to look for\n@return true if the string ends with the suffix"),
        new NamespaceFunction("str", "indexOf", new[] { new BuiltInParam("s", "string"), new BuiltInParam("substring", "string") }, "int",
            Documentation: "Returns the zero-based index of the first occurrence of a substring.\n@param s The string to search in\n@param substring The substring to find\n@return The index of the first occurrence, or -1 if not found"),
        new NamespaceFunction("str", "lastIndexOf", new[] { new BuiltInParam("s", "string"), new BuiltInParam("substring", "string") }, "int",
            Documentation: "Returns the zero-based index of the last occurrence of a substring.\n@param s The string to search in\n@param substring The substring to find\n@return The index of the last occurrence, or -1 if not found"),
        new NamespaceFunction("str", "substring", new[] { new BuiltInParam("s", "string"), new BuiltInParam("start", "int"), new BuiltInParam("end", "int") }, "string", IsVariadic: true,
            Documentation: "Extracts a substring from start index (inclusive) to end index (exclusive). If end is omitted, extracts to the end of the string.\n@param s The input string\n@param start The start index (inclusive)\n@param end The end index (exclusive, optional)\n@return The extracted substring"),
        new NamespaceFunction("str", "replace", new[] { new BuiltInParam("s", "string"), new BuiltInParam("old", "string"), new BuiltInParam("new", "string") }, "string",
            Documentation: "Replaces the first occurrence of a substring with a new string.\n@param s The input string\n@param old The substring to find\n@param new The replacement string\n@return A new string with the first occurrence replaced"),
        new NamespaceFunction("str", "replaceAll", new[] { new BuiltInParam("s", "string"), new BuiltInParam("old", "string"), new BuiltInParam("new", "string") }, "string",
            Documentation: "Replaces all occurrences of a substring with a new string.\n@param s The input string\n@param old The substring to find\n@param new The replacement string\n@return A new string with all occurrences replaced"),
        new NamespaceFunction("str", "split", new[] { new BuiltInParam("s", "string"), new BuiltInParam("delimiter", "string") }, "array",
            Documentation: "Splits a string into an array of substrings separated by the given delimiter.\n@param s The string to split\n@param delimiter The separator string\n@return An array of substrings"),
        new NamespaceFunction("str", "repeat", new[] { new BuiltInParam("s", "string"), new BuiltInParam("count", "int") }, "string",
            Documentation: "Repeats a string the specified number of times.\n@param s The string to repeat\n@param count The number of times to repeat\n@return The repeated string"),
        new NamespaceFunction("str", "reverse", new[] { new BuiltInParam("s", "string") }, "string",
            Documentation: "Reverses the characters in a string.\n@param s The input string\n@return A new string with characters in reverse order"),
        new NamespaceFunction("str", "chars", new[] { new BuiltInParam("s", "string") }, "array",
            Documentation: "Splits a string into an array of individual characters.\n@param s The input string\n@return An array of single-character strings"),
        new NamespaceFunction("str", "padStart", new[] { new BuiltInParam("s", "string"), new BuiltInParam("length", "int"), new BuiltInParam("fill", "string") }, "string", IsVariadic: true,
            Documentation: "Pads the start of a string to the specified length using a fill character. Defaults to space if fill is omitted.\n@param s The input string\n@param length The desired total length\n@param fill The padding character (default: space)\n@return The padded string"),
        new NamespaceFunction("str", "padEnd", new[] { new BuiltInParam("s", "string"), new BuiltInParam("length", "int"), new BuiltInParam("fill", "string") }, "string", IsVariadic: true,
            Documentation: "Pads the end of a string to the specified length using a fill character. Defaults to space if fill is omitted.\n@param s The input string\n@param length The desired total length\n@param fill The padding character (default: space)\n@return The padded string"),
        new NamespaceFunction("str", "isDigit", new[] { new BuiltInParam("s", "string") }, "bool",
            Documentation: "Checks whether all characters in the string are digits (0-9).\n@param s The string to check\n@return true if all characters are digits"),
        new NamespaceFunction("str", "isAlpha", new[] { new BuiltInParam("s", "string") }, "bool",
            Documentation: "Checks whether all characters in the string are letters.\n@param s The string to check\n@return true if all characters are letters"),
        new NamespaceFunction("str", "isAlphaNum", new[] { new BuiltInParam("s", "string") }, "bool",
            Documentation: "Checks whether all characters in the string are alphanumeric (letters or digits).\n@param s The string to check\n@return true if all characters are alphanumeric"),
        new NamespaceFunction("str", "isUpper", new[] { new BuiltInParam("s", "string") }, "bool",
            Documentation: "Checks whether all letter characters in the string are uppercase.\n@param s The string to check\n@return true if all letters are uppercase"),
        new NamespaceFunction("str", "isLower", new[] { new BuiltInParam("s", "string") }, "bool",
            Documentation: "Checks whether all letter characters in the string are lowercase.\n@param s The string to check\n@return true if all letters are lowercase"),
        new NamespaceFunction("str", "isEmpty", new[] { new BuiltInParam("s", "string") }, "bool",
            Documentation: "Checks whether a string is null, empty, or consists only of whitespace.\n@param s The string to check\n@return true if the string is null, empty, or whitespace-only"),
        new NamespaceFunction("str", "match", new[] { new BuiltInParam("s", "string"), new BuiltInParam("pattern", "string") }, "string",
            Documentation: "Returns the first substring matching a regular expression pattern, or null if no match is found.\n@param s The string to search\n@param pattern The regex pattern\n@return The first matching substring, or null"),
        new NamespaceFunction("str", "matchAll", new[] { new BuiltInParam("s", "string"), new BuiltInParam("pattern", "string") }, "array",
            Documentation: "Returns an array of all substrings matching a regular expression pattern.\n@param s The string to search\n@param pattern The regex pattern\n@return An array of all matching substrings"),
        new NamespaceFunction("str", "isMatch", new[] { new BuiltInParam("s", "string"), new BuiltInParam("pattern", "string") }, "bool",
            Documentation: "Checks whether a string contains any match for a regular expression pattern.\n@param s The string to search\n@param pattern The regex pattern\n@return true if the pattern matches anywhere in the string"),
        new NamespaceFunction("str", "replaceRegex", new[] { new BuiltInParam("s", "string"), new BuiltInParam("pattern", "string"), new BuiltInParam("replacement", "string") }, "string",
            Documentation: "Replaces all matches of a regular expression pattern with a replacement string.\n@param s The input string\n@param pattern The regex pattern\n@param replacement The replacement string\n@return A new string with all matches replaced"),
        new NamespaceFunction("str", "count", new[] { new BuiltInParam("s", "string"), new BuiltInParam("substring", "string") }, "int",
            Documentation: "Counts the number of non-overlapping occurrences of a substring.\n@param s The string to search in\n@param substring The substring to count\n@return The count of occurrences"),
        new NamespaceFunction("str", "format", new[] { new BuiltInParam("template", "string"), new BuiltInParam("args") }, "string", IsVariadic: true,
            Documentation: "Formats a string by replacing {0}, {1}, etc. placeholders with the provided arguments.\n@param template The format template with numbered placeholders\n@param args The values to substitute into the placeholders\n@return The formatted string"),
        // assert namespace
        new NamespaceFunction("assert", "equal", new[] { new BuiltInParam("actual"), new BuiltInParam("expected") },
            Documentation: "Asserts that two values are equal. Throws an assertion error if they differ.\n@param actual The actual value\n@param expected The expected value"),
        new NamespaceFunction("assert", "notEqual", new[] { new BuiltInParam("actual"), new BuiltInParam("expected") },
            Documentation: "Asserts that two values are not equal. Throws an assertion error if they are equal.\n@param actual The actual value\n@param expected The value that actual should differ from"),
        new NamespaceFunction("assert", "true", new[] { new BuiltInParam("value") },
            Documentation: "Asserts that a value is truthy. Throws an assertion error if the value is falsy.\n@param value The value to test for truthiness"),
        new NamespaceFunction("assert", "false", new[] { new BuiltInParam("value") },
            Documentation: "Asserts that a value is falsy. Throws an assertion error if the value is truthy.\n@param value The value to test for falsiness"),
        new NamespaceFunction("assert", "null", new[] { new BuiltInParam("value") },
            Documentation: "Asserts that a value is null. Throws an assertion error if the value is not null.\n@param value The value to check"),
        new NamespaceFunction("assert", "notNull", new[] { new BuiltInParam("value") },
            Documentation: "Asserts that a value is not null. Throws an assertion error if the value is null.\n@param value The value to check"),
        new NamespaceFunction("assert", "greater", new[] { new BuiltInParam("a"), new BuiltInParam("b") },
            Documentation: "Asserts that the first value is greater than the second. Throws an assertion error otherwise.\n@param a The value expected to be greater\n@param b The value to compare against"),
        new NamespaceFunction("assert", "less", new[] { new BuiltInParam("a"), new BuiltInParam("b") },
            Documentation: "Asserts that the first value is less than the second. Throws an assertion error otherwise.\n@param a The value expected to be less\n@param b The value to compare against"),
        new NamespaceFunction("assert", "throws", new[] { new BuiltInParam("fn", "function") }, "string",
            Documentation: "Asserts that calling the function throws a runtime error. Throws an assertion error if no error occurs.\n@param fn The function expected to throw\n@return The error message from the thrown error"),
        new NamespaceFunction("assert", "fail", new[] { new BuiltInParam("message", "string") },
            Documentation: "Immediately fails the test with the specified message.\n@param message The failure message"),
        // math namespace
        new NamespaceFunction("math", "abs", new[] { new BuiltInParam("n", "number") }, "number",
            Documentation: "Returns the absolute value of a number.\n@param n The number\n@return The absolute value"),
        new NamespaceFunction("math", "ceil", new[] { new BuiltInParam("n", "number") }, "number",
            Documentation: "Returns the smallest integer greater than or equal to a number (rounds up). Returns float if the input is float, int if the input is int.\n@param n The number to round up\n@return The ceiling value (same type as input)"),
        new NamespaceFunction("math", "floor", new[] { new BuiltInParam("n", "number") }, "number",
            Documentation: "Returns the largest integer less than or equal to a number (rounds down). Returns float if the input is float, int if the input is int.\n@param n The number to round down\n@return The floor value (same type as input)"),
        new NamespaceFunction("math", "round", new[] { new BuiltInParam("n", "number") }, "number",
            Documentation: "Rounds a number to the nearest integer. Ties round away from zero. Returns float if the input is float, int if the input is int.\n@param n The number to round\n@return The rounded value (same type as input)"),
        new NamespaceFunction("math", "min", new[] { new BuiltInParam("a", "number"), new BuiltInParam("b", "number") }, "number",
            Documentation: "Returns the smaller of two numbers.\n@param a The first number\n@param b The second number\n@return The smaller value"),
        new NamespaceFunction("math", "max", new[] { new BuiltInParam("a", "number"), new BuiltInParam("b", "number") }, "number",
            Documentation: "Returns the larger of two numbers.\n@param a The first number\n@param b The second number\n@return The larger value"),
        new NamespaceFunction("math", "pow", new[] { new BuiltInParam("base", "number"), new BuiltInParam("exp", "number") }, "float",
            Documentation: "Raises a number to a power.\n@param base The base number\n@param exp The exponent\n@return The result as a float"),
        new NamespaceFunction("math", "sqrt", new[] { new BuiltInParam("n", "number") }, "float",
            Documentation: "Returns the square root of a number.\n@param n The number (must be non-negative)\n@return The square root as a float"),
        new NamespaceFunction("math", "log", new[] { new BuiltInParam("n", "number") }, "float",
            Documentation: "Returns the natural logarithm (base e) of a number.\n@param n The number (must be positive)\n@return The natural logarithm as a float"),
        new NamespaceFunction("math", "random", Array.Empty<BuiltInParam>(), "float",
            Documentation: "Returns a random float between 0.0 (inclusive) and 1.0 (exclusive).\n@return A random float in [0.0, 1.0)"),
        new NamespaceFunction("math", "randomInt", new[] { new BuiltInParam("min", "int"), new BuiltInParam("max", "int") }, "int",
            Documentation: "Returns a random integer between min (inclusive) and max (inclusive).\n@param min The minimum value (inclusive)\n@param max The maximum value (inclusive)\n@return A random integer in [min, max]"),
        new NamespaceFunction("math", "clamp", new[] { new BuiltInParam("n", "number"), new BuiltInParam("min", "number"), new BuiltInParam("max", "number") }, "number",
            Documentation: "Constrains a number to be within a specified range.\n@param n The number to clamp\n@param min The minimum value\n@param max The maximum value\n@return The clamped value"),
        new NamespaceFunction("math", "sin", new[] { new BuiltInParam("n", "number") }, "float",
            Documentation: "Returns the sine of an angle in radians.\n@param n The angle in radians\n@return The sine value"),
        new NamespaceFunction("math", "cos", new[] { new BuiltInParam("n", "number") }, "float",
            Documentation: "Returns the cosine of an angle in radians.\n@param n The angle in radians\n@return The cosine value"),
        new NamespaceFunction("math", "tan", new[] { new BuiltInParam("n", "number") }, "float",
            Documentation: "Returns the tangent of an angle in radians.\n@param n The angle in radians\n@return The tangent value"),
        new NamespaceFunction("math", "asin", new[] { new BuiltInParam("n", "number") }, "float",
            Documentation: "Returns the arc sine (inverse sine) of a number in radians.\n@param n The value (must be between -1 and 1)\n@return The angle in radians"),
        new NamespaceFunction("math", "acos", new[] { new BuiltInParam("n", "number") }, "float",
            Documentation: "Returns the arc cosine (inverse cosine) of a number in radians.\n@param n The value (must be between -1 and 1)\n@return The angle in radians"),
        new NamespaceFunction("math", "atan", new[] { new BuiltInParam("n", "number") }, "float",
            Documentation: "Returns the arc tangent (inverse tangent) of a number in radians.\n@param n The value\n@return The angle in radians"),
        new NamespaceFunction("math", "atan2", new[] { new BuiltInParam("y", "number"), new BuiltInParam("x", "number") }, "float",
            Documentation: "Returns the angle in radians between the positive x-axis and the point (x, y).\n@param y The y coordinate\n@param x The x coordinate\n@return The angle in radians"),
        new NamespaceFunction("math", "sign", new[] { new BuiltInParam("n", "number") }, "int",
            Documentation: "Returns the sign of a number: -1 for negative, 0 for zero, 1 for positive.\n@param n The number\n@return -1, 0, or 1"),
        new NamespaceFunction("math", "exp", new[] { new BuiltInParam("n", "number") }, "float",
            Documentation: "Returns e raised to the specified power.\n@param n The exponent\n@return The value of e^n"),
        new NamespaceFunction("math", "log10", new[] { new BuiltInParam("n", "number") }, "float",
            Documentation: "Returns the base-10 logarithm of a number.\n@param n The number (must be positive)\n@return The base-10 logarithm"),
        new NamespaceFunction("math", "log2", new[] { new BuiltInParam("n", "number") }, "float",
            Documentation: "Returns the base-2 logarithm of a number.\n@param n The number (must be positive)\n@return The base-2 logarithm"),
        // time namespace
        new NamespaceFunction("time", "now", Array.Empty<BuiltInParam>(), "float",
            Documentation: "Returns the current time as a Unix timestamp (seconds since epoch) with fractional precision.\n@return The current Unix timestamp as a float"),
        new NamespaceFunction("time", "millis", Array.Empty<BuiltInParam>(), "int",
            Documentation: "Returns the current time as Unix milliseconds since epoch.\n@return The current time in milliseconds"),
        new NamespaceFunction("time", "sleep", new[] { new BuiltInParam("seconds", "number") },
            Documentation: "Pauses execution for the specified number of seconds.\n@param seconds The number of seconds to sleep (supports fractional values)"),
        new NamespaceFunction("time", "format", new[] { new BuiltInParam("timestamp", "number"), new BuiltInParam("format", "string") }, "string",
            Documentation: "Formats a Unix timestamp using a format string. Uses .NET date format specifiers.\n@param timestamp The Unix timestamp to format\n@param format The format string (e.g., \"yyyy-MM-dd HH:mm:ss\")\n@return The formatted date/time string"),
        new NamespaceFunction("time", "parse", new[] { new BuiltInParam("str", "string"), new BuiltInParam("format", "string") }, "float",
            Documentation: "Parses a date/time string using a format string and returns a Unix timestamp.\n@param str The date/time string to parse\n@param format The format string matching the input\n@return The Unix timestamp as a float"),
        new NamespaceFunction("time", "date", Array.Empty<BuiltInParam>(), "string",
            Documentation: "Returns today's date in YYYY-MM-DD format.\n@return The current date as a string"),
        new NamespaceFunction("time", "clock", Array.Empty<BuiltInParam>(), "float",
            Documentation: "Returns a high-resolution monotonic timer value in seconds. Useful for measuring elapsed time.\n@return The timer value as a float"),
        new NamespaceFunction("time", "iso", Array.Empty<BuiltInParam>(), "string",
            Documentation: "Returns the current date and time in ISO 8601 format.\n@return The current time as an ISO 8601 string"),
        // json namespace
        new NamespaceFunction("json", "parse", new[] { new BuiltInParam("str", "string") },
            Documentation: "Parses a JSON string into a Stash value (dict, array, string, number, bool, or null).\n@param str The JSON string to parse\n@return The parsed value"),
        new NamespaceFunction("json", "stringify", new[] { new BuiltInParam("value") }, "string",
            Documentation: "Converts a Stash value to a compact JSON string.\n@param value The value to serialize\n@return The JSON string representation"),
        new NamespaceFunction("json", "pretty", new[] { new BuiltInParam("value") }, "string",
            Documentation: "Converts a Stash value to a formatted JSON string with indentation.\n@param value The value to serialize\n@return The pretty-printed JSON string"),
        // http namespace
        new NamespaceFunction("http", "get", new[] { new BuiltInParam("url", "string") }, "HttpResponse",
            Documentation: "Sends an HTTP GET request to the specified URL.\n@param url The URL to request\n@return An HttpResponse with status, body, and headers fields"),
        new NamespaceFunction("http", "post", new[] { new BuiltInParam("url", "string"), new BuiltInParam("body", "string") }, "HttpResponse",
            Documentation: "Sends an HTTP POST request with a body to the specified URL.\n@param url The URL to request\n@param body The request body as a string\n@return An HttpResponse with status, body, and headers fields"),
        new NamespaceFunction("http", "put", new[] { new BuiltInParam("url", "string"), new BuiltInParam("body", "string") }, "HttpResponse",
            Documentation: "Sends an HTTP PUT request with a body to the specified URL.\n@param url The URL to request\n@param body The request body as a string\n@return An HttpResponse with status, body, and headers fields"),
        new NamespaceFunction("http", "delete", new[] { new BuiltInParam("url", "string") }, "HttpResponse",
            Documentation: "Sends an HTTP DELETE request to the specified URL.\n@param url The URL to request\n@return An HttpResponse with status, body, and headers fields"),
        new NamespaceFunction("http", "request", new[] { new BuiltInParam("options", "dict") }, "HttpResponse",
            Documentation: "Sends a configurable HTTP request using an options dictionary. The dict can include keys: \"url\", \"method\", \"headers\" (dict), \"body\" (string).\n@param options A dictionary with request configuration\n@return An HttpResponse with status, body, and headers fields"),
        new NamespaceFunction("http", "patch", new[] { new BuiltInParam("url", "string"), new BuiltInParam("body", "string") }, "HttpResponse",
            Documentation: "Sends an HTTP PATCH request with a body to the specified URL.\n@param url The URL to request\n@param body The request body as a string\n@return An HttpResponse with status, body, and headers fields"),
        new NamespaceFunction("http", "download", new[] { new BuiltInParam("url", "string"), new BuiltInParam("path", "string") },
            Documentation: "Downloads a file from a URL and saves it to disk using streaming (memory-efficient for large files).\n@param url The URL to download from\n@param path The local file path to save to"),
        // ini namespace
        new NamespaceFunction("ini", "parse", new[] { new BuiltInParam("text", "string") }, "dict",
            Documentation: "Parses INI-formatted text into a dictionary. Sections become nested dictionaries.\n@param text The INI text to parse\n@return A dictionary representing the INI structure"),
        new NamespaceFunction("ini", "stringify", new[] { new BuiltInParam("data", "dict") }, "string",
            Documentation: "Converts a dictionary to INI-formatted text.\n@param data The dictionary to serialize\n@return The INI text representation"),
        // config namespace
        new NamespaceFunction("config", "read", new[] { new BuiltInParam("path", "string"), new BuiltInParam("format", "string") }, "dict", IsVariadic: true,
            Documentation: "Reads a configuration file and returns its contents as a dictionary. Auto-detects format from file extension (.json, .ini) or uses the explicit format parameter.\n@param path The path to the configuration file\n@param format Optional format override (\"json\" or \"ini\")\n@return A dictionary representing the configuration"),
        new NamespaceFunction("config", "write", new[] { new BuiltInParam("path", "string"), new BuiltInParam("data"), new BuiltInParam("format", "string") }, null, IsVariadic: true,
            Documentation: "Writes data to a configuration file. Auto-detects format from file extension or uses the explicit format parameter.\n@param path The path to the configuration file\n@param data The data to write\n@param format Optional format override (\"json\" or \"ini\")"),
        new NamespaceFunction("config", "parse", new[] { new BuiltInParam("text", "string"), new BuiltInParam("format", "string") }, "dict",
            Documentation: "Parses configuration text in the specified format.\n@param text The configuration text to parse\n@param format The format to use (\"json\" or \"ini\")\n@return A dictionary representing the configuration"),
        new NamespaceFunction("config", "stringify", new[] { new BuiltInParam("data"), new BuiltInParam("format", "string") }, "string",
            Documentation: "Converts data to a configuration text string in the specified format.\n@param data The data to serialize\n@param format The format to use (\"json\" or \"ini\")\n@return The configuration text"),
        // tpl namespace
        new NamespaceFunction("tpl", "render", new[] { new BuiltInParam("template", "string"), new BuiltInParam("data", "dict") }, "string",
            Documentation: "Renders a template string with the given data dictionary. Supports {{ variable }}, {% if %}, {% for %}, and {% include %} directives.\n@param template The template string or a compiled template\n@param data A dictionary of variables available in the template\n@return The rendered output string"),
        new NamespaceFunction("tpl", "renderFile", new[] { new BuiltInParam("path", "string"), new BuiltInParam("data", "dict") }, "string",
            Documentation: "Reads a template file and renders it with the given data dictionary.\n@param path The path to the template file\n@param data A dictionary of variables available in the template\n@return The rendered output string"),
        new NamespaceFunction("tpl", "compile", new[] { new BuiltInParam("template", "string") },
            Documentation: "Pre-compiles a template string into an optimized form for repeated rendering. Pass the result to tpl.render() for faster execution.\n@param template The template string to compile\n@return A compiled template object"),
        // store namespace
        new NamespaceFunction("store", "set", new[] { new BuiltInParam("key", "string"), new BuiltInParam("value") },
            Documentation: "Sets a key-value pair in the store. Overwrites any existing value for the key.\n@param key The key (must be a string)\n@param value The value to store"),
        new NamespaceFunction("store", "get", new[] { new BuiltInParam("key", "string") },
            Documentation: "Gets the value associated with a key, or null if the key does not exist.\n@param key The key to look up\n@return The stored value, or null"),
        new NamespaceFunction("store", "has", new[] { new BuiltInParam("key", "string") }, "bool",
            Documentation: "Checks whether a key exists in the store.\n@param key The key to check\n@return true if the key exists, false otherwise"),
        new NamespaceFunction("store", "remove", new[] { new BuiltInParam("key", "string") }, "bool",
            Documentation: "Removes a key from the store.\n@param key The key to remove\n@return true if the key was found and removed, false otherwise"),
        new NamespaceFunction("store", "keys", Array.Empty<BuiltInParam>(), "array",
            Documentation: "Returns an array of all keys in the store.\n@return An array of string keys"),
        new NamespaceFunction("store", "values", Array.Empty<BuiltInParam>(), "array",
            Documentation: "Returns an array of all values in the store.\n@return An array of values"),
        new NamespaceFunction("store", "clear", Array.Empty<BuiltInParam>(),
            Documentation: "Removes all entries from the store."),
        new NamespaceFunction("store", "size", Array.Empty<BuiltInParam>(), "int",
            Documentation: "Returns the number of entries in the store.\n@return The number of key-value pairs"),
        new NamespaceFunction("store", "all", Array.Empty<BuiltInParam>(), "dict",
            Documentation: "Returns a dictionary containing all key-value pairs in the store.\n@return A dictionary copy of all store entries"),
        new NamespaceFunction("store", "scope", new[] { new BuiltInParam("prefix", "string") }, "dict",
            Documentation: "Returns a dictionary of all entries whose keys start with the given prefix.\n@param prefix The prefix to filter keys by\n@return A dictionary of matching entries"),
        // ── args ──────────────────────────────────────────────────────
        new NamespaceFunction("args", "list", Array.Empty<BuiltInParam>(), "array",
            Documentation: "Returns an array of all raw command-line arguments passed to the script.\n@return An array of argument strings"),
        new NamespaceFunction("args", "count", Array.Empty<BuiltInParam>(), "int",
            Documentation: "Returns the number of command-line arguments passed to the script.\n@return The argument count"),
        new NamespaceFunction("args", "parse", new[] { new BuiltInParam("spec", "dict") }, "dict",
            Documentation: "Parses command-line arguments according to a dict specification.\n@param spec A dict defining flags, options, commands, and positionals\n@return A dict with all parsed argument values accessible via dot notation"),
        // ── crypto ────────────────────────────────────────────────────
        new NamespaceFunction("crypto", "md5", new[] { new BuiltInParam("data", "string") }, "string",
            Documentation: "Computes the MD5 hash of a string.\n@param data The string to hash\n@return The hash as a lowercase hexadecimal string"),
        new NamespaceFunction("crypto", "sha1", new[] { new BuiltInParam("data", "string") }, "string",
            Documentation: "Computes the SHA-1 hash of a string.\n@param data The string to hash\n@return The hash as a lowercase hexadecimal string"),
        new NamespaceFunction("crypto", "sha256", new[] { new BuiltInParam("data", "string") }, "string",
            Documentation: "Computes the SHA-256 hash of a string.\n@param data The string to hash\n@return The hash as a lowercase hexadecimal string"),
        new NamespaceFunction("crypto", "sha512", new[] { new BuiltInParam("data", "string") }, "string",
            Documentation: "Computes the SHA-512 hash of a string.\n@param data The string to hash\n@return The hash as a lowercase hexadecimal string"),
        new NamespaceFunction("crypto", "hmac", new[] { new BuiltInParam("algo", "string"), new BuiltInParam("key", "string"), new BuiltInParam("data", "string") }, "string",
            Documentation: "Computes an HMAC signature using the specified algorithm.\n@param algo The hash algorithm: \"md5\", \"sha1\", \"sha256\", or \"sha512\"\n@param key The secret key\n@param data The data to sign\n@return The HMAC as a lowercase hexadecimal string"),
        new NamespaceFunction("crypto", "hashFile", new[] { new BuiltInParam("path", "string"), new BuiltInParam("algo", "string") }, "string", IsVariadic: true,
            Documentation: "Computes the hash of a file's contents.\n@param path The file path to hash\n@param algo Optional hash algorithm (default: \"sha256\"). One of \"md5\", \"sha1\", \"sha256\", \"sha512\"\n@return The hash as a lowercase hexadecimal string"),
        new NamespaceFunction("crypto", "uuid", Array.Empty<BuiltInParam>(), "string",
            Documentation: "Generates a random UUID v4 string.\n@return A UUID string in standard format (e.g., \"550e8400-e29b-41d4-a716-446655440000\")"),
        new NamespaceFunction("crypto", "randomBytes", new[] { new BuiltInParam("n", "int") }, "string",
            Documentation: "Generates cryptographically secure random bytes.\n@param n The number of random bytes to generate (must be > 0)\n@return The random bytes as a lowercase hexadecimal string"),
        // ── encoding ─────────────────────────────────────────────────
        new NamespaceFunction("encoding", "base64Encode", new[] { new BuiltInParam("s", "string") }, "string",
            Documentation: "Encodes a string to Base64.\n@param s The string to encode\n@return The Base64-encoded string"),
        new NamespaceFunction("encoding", "base64Decode", new[] { new BuiltInParam("s", "string") }, "string",
            Documentation: "Decodes a Base64 string back to its original string.\n@param s The Base64-encoded string\n@return The decoded string"),
        new NamespaceFunction("encoding", "urlEncode", new[] { new BuiltInParam("s", "string") }, "string",
            Documentation: "URL-encodes a string using RFC 3986 percent-encoding.\n@param s The string to encode\n@return The URL-encoded string"),
        new NamespaceFunction("encoding", "urlDecode", new[] { new BuiltInParam("s", "string") }, "string",
            Documentation: "Decodes a URL-encoded (percent-encoded) string.\n@param s The URL-encoded string\n@return The decoded string"),
        new NamespaceFunction("encoding", "hexEncode", new[] { new BuiltInParam("s", "string") }, "string",
            Documentation: "Encodes a string's UTF-8 bytes as a lowercase hexadecimal string.\n@param s The string to encode\n@return The hexadecimal string"),
        new NamespaceFunction("encoding", "hexDecode", new[] { new BuiltInParam("s", "string") }, "string",
            Documentation: "Decodes a hexadecimal string back to a UTF-8 string.\n@param s The hexadecimal string to decode\n@return The decoded string"),
        // ── term ──────────────────────────────────────────────────────
        new NamespaceFunction("term", "color", new[] { new BuiltInParam("text", "string"), new BuiltInParam("color", "string") }, "string",
            Documentation: "Wraps text in ANSI color codes. Use term color constants: term.BLACK, term.RED, term.GREEN, term.YELLOW, term.BLUE, term.MAGENTA, term.CYAN, term.WHITE, term.GRAY.\n@param text The text to colorize\n@param color A color constant from the term namespace\n@return The text wrapped in ANSI escape codes"),
        new NamespaceFunction("term", "bold", new[] { new BuiltInParam("text", "string") }, "string",
            Documentation: "Wraps text in ANSI bold escape codes.\n@param text The text to make bold\n@return The text with bold formatting"),
        new NamespaceFunction("term", "dim", new[] { new BuiltInParam("text", "string") }, "string",
            Documentation: "Wraps text in ANSI dim escape codes.\n@param text The text to dim\n@return The text with dim formatting"),
        new NamespaceFunction("term", "underline", new[] { new BuiltInParam("text", "string") }, "string",
            Documentation: "Wraps text in ANSI underline escape codes.\n@param text The text to underline\n@return The text with underline formatting"),
        new NamespaceFunction("term", "style", new[] { new BuiltInParam("text", "string"), new BuiltInParam("opts", "dict") }, "string",
            Documentation: "Applies combined ANSI styles from a dict. Supported keys: color (string), bold (bool), dim (bool), underline (bool).\n@param text The text to style\n@param opts A dict with style options\n@return The styled text"),
        new NamespaceFunction("term", "strip", new[] { new BuiltInParam("text", "string") }, "string",
            Documentation: "Removes all ANSI escape codes from a string.\n@param text The text to strip\n@return The text without ANSI escape sequences"),
        new NamespaceFunction("term", "width", Array.Empty<BuiltInParam>(), "int",
            Documentation: "Returns the terminal width in columns. Falls back to 80 if not available.\n@return The terminal width"),
        new NamespaceFunction("term", "isInteractive", Array.Empty<BuiltInParam>(), "bool",
            Documentation: "Checks whether standard input is connected to an interactive terminal (TTY).\n@return true if stdin is a TTY"),
        new NamespaceFunction("term", "clear", Array.Empty<BuiltInParam>(),
            Documentation: "Clears the terminal screen using ANSI escape codes."),
        new NamespaceFunction("term", "table", new[] { new BuiltInParam("rows", "array"), new BuiltInParam("headers", "array") }, "string", IsVariadic: true,
            Documentation: "Formats data as an ASCII table string. Each row is an array of values. Headers are optional.\n@param rows An array of arrays (each inner array is a row)\n@param headers An optional array of column header strings\n@return The formatted table as a string"),
        // sys namespace ──────────────────────────────────────────────────────
        new NamespaceFunction("sys", "cpuCount", Array.Empty<BuiltInParam>(), "int",
            Documentation: "Returns the number of logical CPU cores.\n@return The number of CPU cores as an integer"),
        new NamespaceFunction("sys", "totalMemory", Array.Empty<BuiltInParam>(), "int",
            Documentation: "Returns the total physical RAM in bytes.\n@return Total memory in bytes"),
        new NamespaceFunction("sys", "freeMemory", Array.Empty<BuiltInParam>(), "int",
            Documentation: "Returns the available free RAM in bytes. On Linux, reads from /proc/meminfo.\n@return Available memory in bytes"),
        new NamespaceFunction("sys", "uptime", Array.Empty<BuiltInParam>(), "float",
            Documentation: "Returns the system uptime in seconds.\n@return Uptime in seconds as a float"),
        new NamespaceFunction("sys", "loadAvg", Array.Empty<BuiltInParam>(), "array",
            Documentation: "Returns CPU load averages as an array [1min, 5min, 15min]. On non-Linux platforms, returns [0.0, 0.0, 0.0].\n@return An array of three float values"),
        new NamespaceFunction("sys", "diskUsage", new[] { new BuiltInParam("path", "string") }, "dict", IsVariadic: true,
            Documentation: "Returns disk usage information for the given path as a dict with 'total', 'used', and 'free' keys (all in bytes). Defaults to root filesystem if no path is provided.\n@param path Optional path to check disk usage for\n@return A dict with total, used, and free bytes"),
        new NamespaceFunction("sys", "pid", Array.Empty<BuiltInParam>(), "int",
            Documentation: "Returns the current process ID.\n@return The PID as an integer"),
        new NamespaceFunction("sys", "tempDir", Array.Empty<BuiltInParam>(), "string",
            Documentation: "Returns the OS temporary directory path.\n@return The path to the temp directory"),
        new NamespaceFunction("sys", "networkInterfaces", Array.Empty<BuiltInParam>(), "array",
            Documentation: "Returns an array of network interface dicts, each with 'name', 'type', 'status', and 'addresses' fields.\n@return An array of dicts describing network interfaces"),
    };

    // ── Built-in Namespace Constants ──

    public static readonly IReadOnlyList<NamespaceConstant> NamespaceConstants = new[]
    {
        new NamespaceConstant("process", "SIGHUP",  "int", "1",
            Documentation: "Hangup signal (1). Sent when a terminal is closed or a controlling process ends."),
        new NamespaceConstant("process", "SIGINT",  "int", "2",
            Documentation: "Interrupt signal (2). Sent when the user presses Ctrl+C."),
        new NamespaceConstant("process", "SIGQUIT", "int", "3",
            Documentation: "Quit signal (3). Similar to SIGINT but also produces a core dump."),
        new NamespaceConstant("process", "SIGKILL", "int", "9",
            Documentation: "Kill signal (9). Immediately terminates a process. Cannot be caught or ignored."),
        new NamespaceConstant("process", "SIGUSR1", "int", "10",
            Documentation: "User-defined signal 1 (10). Available for custom application-level signaling."),
        new NamespaceConstant("process", "SIGUSR2", "int", "12",
            Documentation: "User-defined signal 2 (12). Available for custom application-level signaling."),
        new NamespaceConstant("process", "SIGTERM", "int", "15",
            Documentation: "Termination signal (15). Requests graceful shutdown. Can be caught for cleanup."),
        new NamespaceConstant("math", "PI", "float", "3.141592653589793",
            Documentation: "The ratio of a circle's circumference to its diameter (π ≈ 3.14159)."),
        new NamespaceConstant("math", "E",  "float", "2.718281828459045",
            Documentation: "Euler's number, the base of natural logarithms (e ≈ 2.71828)."),
        new NamespaceConstant("term", "BLACK",   "string", "black",
            Documentation: "Color constant for black. Use with term.color() or term.style()."),
        new NamespaceConstant("term", "RED",     "string", "red",
            Documentation: "Color constant for red. Use with term.color() or term.style()."),
        new NamespaceConstant("term", "GREEN",   "string", "green",
            Documentation: "Color constant for green. Use with term.color() or term.style()."),
        new NamespaceConstant("term", "YELLOW",  "string", "yellow",
            Documentation: "Color constant for yellow. Use with term.color() or term.style()."),
        new NamespaceConstant("term", "BLUE",    "string", "blue",
            Documentation: "Color constant for blue. Use with term.color() or term.style()."),
        new NamespaceConstant("term", "MAGENTA", "string", "magenta",
            Documentation: "Color constant for magenta. Use with term.color() or term.style()."),
        new NamespaceConstant("term", "CYAN",    "string", "cyan",
            Documentation: "Color constant for cyan. Use with term.color() or term.style()."),
        new NamespaceConstant("term", "WHITE",   "string", "white",
            Documentation: "Color constant for white. Use with term.color() or term.style()."),
        new NamespaceConstant("term", "GRAY",    "string", "gray",
            Documentation: "Color constant for gray. Use with term.color() or term.style()."),
    };

    // ── Built-in Namespace Names ──

    public static readonly IReadOnlyList<string> NamespaceNames = new[]
    {
        "io", "conv", "env", "process", "fs", "path", "arr", "dict", "str", "assert", "math", "time", "json", "http", "ini", "config", "tpl", "store", "args", "crypto", "encoding", "term", "sys"
    };

    // ── Keywords ──

    public static readonly IReadOnlyList<string> Keywords = new[]
    {
        "let", "const", "fn", "struct", "enum", "if", "else",
        "for", "in", "while", "do", "return", "break", "continue",
        "true", "false", "null", "try", "import", "from", "as", "switch",
        "and", "or", "args"
    };

    // ── Valid built-in type names (for type hint validation) ──

    public static readonly HashSet<string> ValidTypes = new()
    {
        "string", "int", "float", "bool", "null", "array",
        "dict", "function", "namespace", "range"
    };

    // ── Known names for semantic validation (don't warn as undefined) ──

    public static readonly HashSet<string> KnownNames = new(
        Functions.Select(f => f.Name)
            .Concat(Structs.Select(s => s.Name))
            .Concat(NamespaceNames)
            .Concat(new[] { "args", "true", "false", "null", "println", "print", "readLine" })
    );

    // ── Precomputed lookup tables ──

    private static readonly Dictionary<string, BuiltInFunction> _functionsByName =
        Functions.ToDictionary(f => f.Name);

    private static readonly Dictionary<string, NamespaceFunction> _namespaceFunctionsByQualifiedName =
        NamespaceFunctions.ToDictionary(f => f.QualifiedName);

    private static readonly Dictionary<string, NamespaceConstant> _namespaceConstantsByQualifiedName =
        NamespaceConstants.ToDictionary(c => c.QualifiedName);

    private static readonly HashSet<string> _builtInFunctionNames =
        new(Functions.Select(f => f.Name).Concat(new[] { "println", "print", "readLine" }));

    private static readonly HashSet<string> _namespaceNameSet = new(NamespaceNames);

    private static readonly Dictionary<string, IReadOnlyList<NamespaceFunction>> _namespaceMembersByNamespace =
        NamespaceFunctions.GroupBy(f => f.Namespace).ToDictionary(g => g.Key, g => (IReadOnlyList<NamespaceFunction>)g.ToList());

    private static readonly Dictionary<string, IReadOnlyList<NamespaceConstant>> _namespaceConstantsByNamespace =
        NamespaceConstants.GroupBy(c => c.Namespace).ToDictionary(g => g.Key, g => (IReadOnlyList<NamespaceConstant>)g.ToList());

    public static bool TryGetFunction(string name, out BuiltInFunction function)
        => _functionsByName.TryGetValue(name, out function!);

    public static bool TryGetNamespaceFunction(string qualifiedName, out NamespaceFunction function)
        => _namespaceFunctionsByQualifiedName.TryGetValue(qualifiedName, out function!);

    public static IEnumerable<NamespaceFunction> GetNamespaceMembers(string namespaceName)
        => _namespaceMembersByNamespace.TryGetValue(namespaceName, out var members) ? members : [];

    public static IEnumerable<NamespaceConstant> GetNamespaceConstants(string namespaceName)
        => _namespaceConstantsByNamespace.TryGetValue(namespaceName, out var constants) ? constants : [];

    public static bool TryGetNamespaceConstant(string qualifiedName, out NamespaceConstant constant)
        => _namespaceConstantsByQualifiedName.TryGetValue(qualifiedName, out constant!);

    public static bool IsBuiltInFunction(string name) => _builtInFunctionNames.Contains(name);

    public static bool IsBuiltInNamespace(string name) => _namespaceNameSet.Contains(name);
}
