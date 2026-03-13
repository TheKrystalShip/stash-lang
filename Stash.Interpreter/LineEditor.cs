using System;
using System.Collections.Generic;
using System.Text;

namespace Stash;

public class LineEditor
{
    private readonly List<string> _history = new();
    private int _historyIndex;
    private string _savedCurrentLine = "";

    private StringBuilder _buffer = new();
    private int _cursor;
    private string _prompt = "";
    private int _previousLength;

    public string? ReadLine(string prompt)
    {
        _prompt = prompt;
        _buffer.Clear();
        _cursor = 0;
        _previousLength = 0;
        _historyIndex = _history.Count;
        _savedCurrentLine = "";

        Console.Write(prompt);

        while (true)
        {
            var key = Console.ReadKey(true);

            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    Console.WriteLine();
                    var result = _buffer.ToString();
                    if (result.Length > 0 && (_history.Count == 0 || _history[^1] != result))
                    {
                        _history.Add(result);
                    }
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

    private void Render()
    {
        // Return to column 0
        Console.Write('\r');
        // Rewrite prompt + buffer
        Console.Write(_prompt);
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

    private void MoveCursorToPosition()
    {
        int targetCol = _prompt.Length + _cursor;
        Console.Write('\r');
        if (targetCol > 0)
        {
            Console.Write($"\x1b[{targetCol}C");
        }
    }

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

    private void SetBuffer(string text)
    {
        _buffer.Clear();
        _buffer.Append(text);
        _cursor = _buffer.Length;
        Render();
    }

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
