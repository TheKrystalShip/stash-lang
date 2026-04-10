using System.Diagnostics;
using Stash.Bytecode;
using Stash.Runtime;

namespace Stash.Playground.Services;

public sealed class PlaygroundExecutor
{
    private const long DefaultStepLimit = 5_000_000;
    private const long MaxOutputLength = 512 * 1024; // 512 KB

    public PlaygroundResult Execute(string code)
    {
        var output = new CappedStringWriter(MaxOutputLength);
        var errors = new StringWriter();

        var engine = new StashEngine(StashCapabilities.None);
        engine.Output = output;
        engine.ErrorOutput = errors;
        engine.StepLimit = DefaultStepLimit;

        var sw = Stopwatch.StartNew();

        try
        {
            ExecutionResult result = engine.Run(code);
            sw.Stop();

            string outputText = output.ToString();
            if (output.IsTruncated)
            {
                outputText += "\n\n... output truncated (512 KB limit) ...";
            }

            if (result.Errors.Count > 0)
            {
                return new PlaygroundResult
                {
                    Output = outputText,
                    ErrorMessages = result.Errors,
                    ElapsedMs = sw.Elapsed.TotalMilliseconds,
                    StepCount = engine.StepCount,
                    Success = false,
                };
            }

            return new PlaygroundResult
            {
                Output = outputText,
                ElapsedMs = sw.Elapsed.TotalMilliseconds,
                StepCount = engine.StepCount,
                Success = true,
            };
        }
        catch (StepLimitExceededException ex)
        {
            sw.Stop();
            return new PlaygroundResult
            {
                Output = output.ToString(),
                ErrorMessages = [$"Execution exceeded the step limit of {ex.StepLimit:N0} statements."],
                ElapsedMs = sw.Elapsed.TotalMilliseconds,
                StepCount = engine.StepCount,
                Success = false,
                StepLimitExceeded = true,
            };
        }
        catch (ScriptCancelledException)
        {
            sw.Stop();
            return new PlaygroundResult
            {
                Output = output.ToString(),
                ErrorMessages = ["Execution was cancelled."],
                ElapsedMs = sw.Elapsed.TotalMilliseconds,
                StepCount = engine.StepCount,
                Success = false,
                TimedOut = true,
            };
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return new PlaygroundResult
            {
                Output = output.ToString(),
                ErrorMessages = ["Execution was cancelled."],
                ElapsedMs = sw.Elapsed.TotalMilliseconds,
                StepCount = engine.StepCount,
                Success = false,
                TimedOut = true,
            };
        }
        catch (RuntimeError ex)
        {
            sw.Stop();
            string errorMsg = ex.Span is { } spanVal
                ? $"[Line {spanVal.StartLine}:{spanVal.StartColumn}] {ex.Message}"
                : ex.Message;

            return new PlaygroundResult
            {
                Output = output.ToString(),
                ErrorMessages = [errorMsg],
                ElapsedMs = sw.Elapsed.TotalMilliseconds,
                StepCount = engine.StepCount,
                Success = false,
            };
        }
    }
}
