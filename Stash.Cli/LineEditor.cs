using System;
using System.Collections.Generic;
using System.Text;
using Stash.Cli.Completion;
using Stash.Cli.History;
using Stash.Cli.Repl;

namespace Stash;

/// <summary>Interactive line editor with history, word-level cursor movement, and inline editing for the REPL.</summary>
public class LineEditor
{
    /// <summary>Previously entered lines.</summary>
    private readonly List<string> _history = new();
    /// <summary>Current position in history during navigation.</summary>
    private int _historyIndex;
    /// <summary>Saved in-progress line when navigating history.</summary>
    private string _savedCurrentLine = "";

    /// <summary>Current line content being edited.</summary>
    private StringBuilder _buffer = new();
    /// <summary>Cursor position within the buffer.</summary>
    private int _cursor;
    /// <summary>The prompt string displayed before input.</summary>
    private string _prompt = "";
    /// <summary>The portion of <see cref="_prompt"/> that lives on the cursor's line (everything after the last <c>\n</c>).</summary>
    private string _promptLastLine = "";
    /// <summary>Visible width of the prompt's last line after stripping ANSI escapes and zero-width markers.</summary>
    private int _promptVisibleWidth;
    /// <summary>Length of previously rendered line for clearing leftovers.</summary>
    private int _previousLength;
    /// <summary>True when the previous key was Tab (used by the bash-classic state machine).</summary>
    private bool _lastKeyWasTab;

    /// <summary>History sink for persistent file storage. Null means persistence disabled.</summary>
    internal IHistorySink? HistorySink { get; set; }

    /// <summary>Seeds the in-memory history list from a pre-loaded snapshot (oldest-first).</summary>
    internal void SeedHistory(IReadOnlyList<string> initial)
    {
        _history.Clear();
        _history.AddRange(initial);
    }

    /// <summary>
    /// The completion engine to use when Tab is pressed.
    /// When <c>null</c>, Tab inserts a literal tab character.
    /// </summary>
    internal CompletionEngine? CompletionEngine { get; set; }

    /// <summary>
    /// When <c>false</c> (e.g. <c>STASH_NO_COMPLETION=1</c>), Tab inserts a literal
    /// tab character instead of triggering completion.
    /// </summary>
    public bool CompletionEnabled { get; set; } = true;

    /// <summary>Reads a line with interactive editing (arrows, history, word movement).</summary>
    /// <param name="prompt">The prompt string to display before input.</param>
    /// <returns>The entered line, or <c>null</c> on EOF.</returns>
    public string? ReadLine(string prompt)
    {
        _prompt = prompt;
        // Split off any header lines (everything up to and including the last '\n').
        // Only the last line is re-rendered on each keystroke — header lines are
        // printed once and remain on the scrollback above the editing line.
        int lastNl = prompt.LastIndexOf('\n');
        _promptLastLine = lastNl >= 0 ? prompt.Substring(lastNl + 1) : prompt;
        _promptVisibleWidth = PromptWidthCalculator.VisibleWidth(_promptLastLine);
        _buffer.Clear();
        _cursor = 0;
        _previousLength = 0;
        _historyIndex = _history.Count;
        _savedCurrentLine = "";
        _lastKeyWasTab = false;

        Console.Write(prompt);

        while (true)
        {
            var key = Console.ReadKey(true);

            // Reset _lastKeyWasTab on any non-Tab key so the user must double-Tab
            // again to see the candidate list (bash-classic state machine, spec §4).
            bool wasTab = key.Key == ConsoleKey.Tab;
            if (!wasTab)
                _lastKeyWasTab = false;

            switch (key.Key)
            {
                case ConsoleKey.Tab:
                    if (!CompletionEnabled || CompletionEngine is null)
                    {
                        // Completion disabled or unavailable → insert literal tab
                        _buffer.Insert(_cursor, '\t');
                        _cursor++;
                        Render();
                    }
                    else
                    {
                        var actionResult = TabCompletionAction.Apply(
                            _buffer,
                            ref _cursor,
                            ref _lastKeyWasTab,
                            CompletionEngine,
                            candidates =>
                            {
                                Console.WriteLine();
                                CompletionMenu.Render(candidates,
                                    msg => CompletionEngine.PromptYesNo(msg));
                                Console.WriteLine();
                                Render();
                            });

                        switch (actionResult.Kind)
                        {
                            case TabActionKind.Bell:
                                Console.Error.Write('\x07');
                                break;
                            case TabActionKind.Modified:
                                Render();
                                break;
                            case TabActionKind.NoOp:
                            case TabActionKind.ListedCandidates:
                                // ListedCandidates: menu + redraw handled by the lambda above.
                                // NoOp: nothing to do.
                                break;
                        }
                    }
                    break;
                case ConsoleKey.Enter:
                    Console.WriteLine();
                    var result = _buffer.ToString();
                    if (result.Length > 0 && result[0] != ' ' && (_history.Count == 0 || _history[^1] != result))
                    {
                        _history.Add(result);
                    }
                    HistorySink?.Append(result);
                    return result;

                case ConsoleKey.Backspace:
                    if (_cursor > 0)
                    {
                        _buffer.Remove(_cursor - 1, 1);
                        _cursor--;
                        Render();
                    }
                    break;

                case ConsoleKey.Delete:
                    if (_cursor < _buffer.Length)
                    {
                        _buffer.Remove(_cursor, 1);
                        Render();
                    }
                    break;

                case ConsoleKey.LeftArrow:
                    if ((key.Modifiers & ConsoleModifiers.Control) != 0)
                    {
                        WordLeft();
                    }
                    else if (_cursor > 0)
                    {
                        _cursor--;
                        Render();
                    }
                    break;

                case ConsoleKey.RightArrow:
                    if ((key.Modifiers & ConsoleModifiers.Control) != 0)
                    {
                        WordRight();
                    }
                    else if (_cursor < _buffer.Length)
                    {
                        _cursor++;
                        Render();
                    }
                    break;

                case ConsoleKey.UpArrow:
                    HistoryPrevious();
                    break;

                case ConsoleKey.DownArrow:
                    HistoryNext();
                    break;

                case ConsoleKey.Home:
                    _cursor = 0;
                    Render();
                    break;

                case ConsoleKey.End:
                    _cursor = _buffer.Length;
                    Render();
                    break;

                default:
                    // Ctrl+key combinations
                    if ((key.Modifiers & ConsoleModifiers.Control) != 0)
                    {
                        switch (key.Key)
                        {
                            case ConsoleKey.A: // Home
                                _cursor = 0;
                                Render();
                                break;

                            case ConsoleKey.E: // End
                                _cursor = _buffer.Length;
                                Render();
                                break;

                            case ConsoleKey.U: // Kill to beginning
                                _buffer.Remove(0, _cursor);
                                _cursor = 0;
                                Render();
                                break;

                            case ConsoleKey.K: // Kill to end
                                _buffer.Remove(_cursor, _buffer.Length - _cursor);
                                Render();
                                break;

                            case ConsoleKey.W: // Delete word backward
                                DeleteWordBackward();
                                break;

                            case ConsoleKey.D: // EOF on empty line
                                if (_buffer.Length == 0)
                                {
                                    Console.WriteLine();
                                    return null;
                                }
                                // Otherwise behave like Delete
                                if (_cursor < _buffer.Length)
                                {
                                    _buffer.Remove(_cursor, 1);
                                    Render();
                                }
                                break;

                            case ConsoleKey.C: // Cancel line
                                Console.Write("^C");
                                Console.WriteLine();
                                _buffer.Clear();
                                _cursor = 0;
                                _previousLength = 0;
                                Console.Write(_prompt);
                                break;

                            case ConsoleKey.L: // Clear screen
                                Console.Write("\x1b[2J\x1b[H"); // ANSI clear screen + home
                                Console.Write(_prompt);
                                Console.Write(_buffer.ToString());
                                _previousLength = _buffer.Length;
                                // Reposition cursor
                                MoveCursorToPosition();
                                break;
                        }
                    }
                    else if (key.KeyChar >= ' ') // Printable character
                    {
                        _buffer.Insert(_cursor, key.KeyChar);
                        _cursor++;
                        Render();
                    }
                    break;
            }
        }
    }

    /// <summary>Re-renders buffer to console.</summary>
    private void Render()
    {
        // Return to column 0
        Console.Write('\r');
        // Rewrite only the last line of the prompt + buffer. Any header lines
        // (everything before the last '\n' of the original prompt) were
        // printed once in ReadLine and stay on the scrollback above.
        Console.Write(_promptLastLine);
        var text = _buffer.ToString();
        Console.Write(text);
        // Clear leftover chars from previous longer content
        int clearCount = _previousLength - text.Length;
        if (clearCount > 0)
        {
            Console.Write(new string(' ', clearCount));
        }

        _previousLength = text.Length;
        // Position cursor at edit point
        MoveCursorToPosition();
    }

    /// <summary>Positions console cursor to match buffer cursor.</summary>
    private void MoveCursorToPosition()
    {
        int targetCol = _promptVisibleWidth + _cursor;
        Console.Write('\r');
        if (targetCol > 0)
        {
            Console.Write($"\x1b[{targetCol}C");
        }
    }

    /// <summary>Navigates to previous history entry.</summary>
    private void HistoryPrevious()
    {
        if (_historyIndex <= 0)
        {
            return;
        }

        if (_historyIndex == _history.Count)
        {
            _savedCurrentLine = _buffer.ToString();
        }

        _historyIndex--;
        SetBuffer(_history[_historyIndex]);
    }

    /// <summary>Navigates to next history entry or restores saved line.</summary>
    private void HistoryNext()
    {
        if (_historyIndex >= _history.Count)
        {
            return;
        }

        _historyIndex++;
        if (_historyIndex == _history.Count)
        {
            SetBuffer(_savedCurrentLine);
        }
        else
        {
            SetBuffer(_history[_historyIndex]);
        }
    }

    /// <summary>Replaces buffer and moves cursor to end.</summary>
    private void SetBuffer(string text)
    {
        _buffer.Clear();
        _buffer.Append(text);
        _cursor = _buffer.Length;
        Render();
    }

    /// <summary>
    /// Moves the cursor one word to the left (bound to <c>Ctrl+Left</c>).
    /// </summary>
    /// <remarks>
    /// Skips trailing non-alphanumeric characters first, then skips the preceding
    /// alphanumeric word, leaving the cursor at the start of that word.
    /// </remarks>
    private void WordLeft()
    {
        if (_cursor == 0)
        {
            return;
        }
        // Skip whitespace
        while (_cursor > 0 && !char.IsLetterOrDigit(_buffer[_cursor - 1]))
        {
            _cursor--;
        }
        // Skip word
        while (_cursor > 0 && char.IsLetterOrDigit(_buffer[_cursor - 1]))
        {
            _cursor--;
        }

        Render();
    }

    /// <summary>
    /// Moves the cursor one word to the right (bound to <c>Ctrl+Right</c>).
    /// </summary>
    /// <remarks>
    /// Skips the current alphanumeric word first, then skips any following
    /// non-alphanumeric characters, leaving the cursor at the start of the next word.
    /// </remarks>
    private void WordRight()
    {
        if (_cursor >= _buffer.Length)
        {
            return;
        }
        // Skip current word
        while (_cursor < _buffer.Length && char.IsLetterOrDigit(_buffer[_cursor]))
        {
            _cursor++;
        }
        // Skip whitespace
        while (_cursor < _buffer.Length && !char.IsLetterOrDigit(_buffer[_cursor]))
        {
            _cursor++;
        }

        Render();
    }

    /// <summary>
    /// Deletes one word backward from the cursor (bound to <c>Ctrl+W</c>).
    /// </summary>
    /// <remarks>
    /// Mirrors the navigation logic of <see cref="WordLeft"/>: first skips trailing
    /// non-alphanumeric characters, then skips the preceding alphanumeric word, and
    /// removes the entire skipped range from the buffer.
    /// </remarks>
    private void DeleteWordBackward()
    {
        if (_cursor == 0)
        {
            return;
        }

        int end = _cursor;
        // Skip whitespace
        while (_cursor > 0 && !char.IsLetterOrDigit(_buffer[_cursor - 1]))
        {
            _cursor--;
        }
        // Skip word
        while (_cursor > 0 && char.IsLetterOrDigit(_buffer[_cursor - 1]))
        {
            _cursor--;
        }

        _buffer.Remove(_cursor, end - _cursor);
        Render();
    }
}
