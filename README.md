# Stash

A C-style interpreted shell scripting language that combines shell scripting power with structured data capabilities.

## Building

```bash
dotnet build Stash/
```

## Running

```bash
# Start the REPL
dotnet run --project Stash/

# Or after building
./Stash/bin/Debug/net10.0/stash
```

## Status

**Phase 1 — Foundation** (Complete)
- Lexer with full token scanning
- Recursive descent parser for expressions
- Tree-walk interpreter
- Interactive REPL

See [Stash — Language Specification.md](docs/Stash%20%E2%80%94%20Language%20Specification.md) for the full language design.

## License

GPL3 [LICENSE](LICENSE)
