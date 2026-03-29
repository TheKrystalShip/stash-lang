# @stash/docker

A comprehensive Docker CLI wrapper for [Stash](https://github.com/stash-lang/stash), providing idiomatic access to the full Docker CLI from Stash scripts.

## Installation

```bash
stash pkg install @stash/docker
```

## Quick Start

```stash
import "@stash/docker" as docker;

// Check Docker is available
if (!docker.system.available()) {
    io.println("Docker is not installed or not running.");
    exit(1);
}

// Run a container
let result = docker.containers.run("nginx:latest", {
    name: "my-nginx",
    detach: true,
    ports: ["8080:80"],
});

if (result.ok) {
    io.println("Container started: " + str.trim(result.stdout));
}

// List running containers
let ps = docker.containers.ls();
if (ps.ok) {
    for (let c in ps.data) {
        io.println($"{c.Names} — {c.Status}");
    }
}

// Stop and remove
docker.containers.stop("my-nginx");
docker.containers.rm("my-nginx");
```

You can also import individual modules directly:

```stash
import "lib/containers.stash" as containers;
import "lib/images.stash" as images;
```

## Modules

### containers

Full container lifecycle management — 24 functions.

| Function | Description |
|---|---|
| `run(image, opts)` | Create and start a container |
| `create(image, opts)` | Create a container without starting |
| `start(container, opts)` | Start a stopped container |
| `stop(container, opts)` | Stop a running container |
| `restart(container, opts)` | Restart a container |
| `kill(container, opts)` | Send a signal to a container |
| `rm(container, opts)` | Remove a container |
| `pause(container)` | Pause a container |
| `unpause(container)` | Unpause a container |
| `rename(container, new_name)` | Rename a container |
| `wait(container)` | Block until a container stops |
| `ls(opts)` | List containers (JSON) |
| `inspect(container)` | Get detailed container info (JSON) |
| `logs(container, opts)` | Fetch container logs |
| `top(container)` | List running processes |
| `stats(containers, opts)` | Live resource usage (one-shot by default) |
| `diff(container)` | Show filesystem changes |
| `port(container, private_port)` | Show port mappings |
| `container_exec(container, cmd, opts)` | Execute a command in a running container |
| `cp(src, dst)` | Copy files between container and host |
| `commit(container, repo, opts)` | Create an image from a container |
| `export(container, output_path)` | Export container filesystem as tar |
| `update(container, opts)` | Update container resource limits |
| `prune(opts)` | Remove stopped containers |

#### Common Options for `run`

```stash
docker.containers.run("ubuntu:22.04", {
    name: "dev-box",
    detach: true,
    env: { NODE_ENV: "production", PORT: "3000" },
    ports: ["3000:3000", "9229:9229"],
    volumes: ["./app:/app"],
    labels: { team: "backend" },
    network: "my-net",
    restart: "unless-stopped",
    memory: "512m",
    cpus: "1.5",
    user: "node",
    workdir: "/app",
    command: "node server.js",
});
```

### images

Image management — 14 functions.

| Function | Description |
|---|---|
| `ls(opts)` | List local images (JSON) |
| `pull(image, opts)` | Pull an image from a registry |
| `push(image, opts)` | Push an image to a registry |
| `build(path, opts)` | Build an image from a Dockerfile |
| `tag(source, target)` | Tag an image |
| `rmi(image, opts)` | Remove an image |
| `inspect(image)` | Get detailed image info (JSON) |
| `history(image, opts)` | Show image layers (JSON) |
| `save(images, output_path)` | Save images to a tar archive |
| `load(input_path, opts)` | Load images from a tar archive |
| `import_image(source, repo, opts)` | Import a tarball as an image |
| `prune(opts)` | Remove unused images |
| `search(term, opts)` | Search Docker Hub (JSON) |

#### Build Example

```stash
docker.images.build(".", {
    tag: "myapp:latest",
    file: "Dockerfile.prod",
    build_arg: { VERSION: "1.2.3" },
    no_cache: true,
    platform: "linux/amd64",
});
```

### volumes

Volume management — 5 functions.

| Function | Description |
|---|---|
| `create(name, opts)` | Create a volume |
| `rm(volume, opts)` | Remove a volume |
| `ls(opts)` | List volumes (JSON) |
| `inspect(volume)` | Get volume details (JSON) |
| `prune(opts)` | Remove unused volumes |

### networks

Network management — 7 functions.

| Function | Description |
|---|---|
| `create(name, opts)` | Create a network |
| `rm(network)` | Remove a network |
| `ls(opts)` | List networks (JSON) |
| `inspect(network)` | Get network details (JSON) |
| `connect(network, container, opts)` | Connect a container to a network |
| `disconnect(network, container, opts)` | Disconnect a container from a network |
| `prune(opts)` | Remove unused networks |

### compose

Docker Compose wrapper — 24 functions.

| Function | Description |
|---|---|
| `up(opts)` | Create and start services |
| `down(opts)` | Stop and remove services |
| `build(opts)` | Build service images |
| `ps(opts)` | List service containers (JSON) |
| `logs(opts)` | View service logs |
| `compose_exec(service, cmd, opts)` | Execute in a running service |
| `run(service, cmd, opts)` | Run a one-off command |
| `restart(opts)` | Restart services |
| `stop(opts)` | Stop services |
| `start(opts)` | Start services |
| `pull(opts)` | Pull service images |
| `push(opts)` | Push service images |
| `kill(opts)` | Force stop services |
| `rm(opts)` | Remove stopped services |
| `config(opts)` | Validate and view config |
| `top(opts)` | Display running processes |
| `images(opts)` | List images used by services |
| `port(service, port, opts)` | Show port binding |
| `pause(opts)` | Pause services |
| `unpause(opts)` | Unpause services |
| `create(opts)` | Create services without starting |
| `cp(src, dst, opts)` | Copy files to/from services |

All compose functions accept project-level options: `file` (string or array of `-f` paths), `project_name` (string for `-p`), `project_directory`, `env_file`, `profile`.

#### Compose Example

```stash
// Start a project
docker.compose.up({
    file: "docker-compose.prod.yml",
    project_name: "myapp",
    detach: true,
    build: true,
});

// Check running services
let services = docker.compose.ps({ project_name: "myapp" });
if (services.ok) {
    for (let svc in services.data) {
        io.println($"{svc.Name}: {svc.State}");
    }
}

// View logs
docker.compose.logs({
    project_name: "myapp",
    service: "api",
    tail: "50",
    follow: true,
});

// Tear down
docker.compose.down({
    project_name: "myapp",
    volumes: true,
    remove_orphans: true,
});
```

### system

System-level Docker operations — 7 functions.

| Function | Description |
|---|---|
| `info(opts)` | System-wide Docker info (JSON) |
| `version(opts)` | Client/server version info (JSON) |
| `df(opts)` | Disk usage (JSON) |
| `ping()` | Check if Docker daemon is responsive |
| `events(opts)` | Get Docker event stream |
| `prune(opts)` | Remove all unused data |
| `available()` | Check if Docker CLI is installed |

### registry

Registry authentication — 3 functions.

| Function | Description |
|---|---|
| `login(server, opts)` | Log in to a registry |
| `logout(server)` | Log out from a registry |
| `search(term, opts)` | Search Docker Hub (JSON) |

### context

Context management — 8 functions.

| Function | Description |
|---|---|
| `create(name, opts)` | Create a context |
| `rm(name, opts)` | Remove contexts |
| `ls(opts)` | List contexts (JSON) |
| `inspect(name)` | Inspect a context (JSON) |
| `use(name)` | Switch active context |
| `export_context(name, path)` | Export a context to file |
| `import_context(name, path)` | Import a context from file |
| `update(name, opts)` | Update a context |

## Return Values

All functions return a result dict:

**Standard result** (from `exec`):
```stash
{
    ok: true,        // true if exitCode == 0
    stdout: "...",   // raw stdout
    stderr: "...",   // raw stderr
    exitCode: 0,     // process exit code
}
```

**JSON result** (from `exec_json` / `exec_json_lines`):
```stash
{
    ok: true,        // true if exitCode == 0 and JSON parsed successfully
    data: {...},     // parsed JSON (object, array, etc.)
    stderr: "...",
    exitCode: 0,
}
```

## Options Convention

Options are passed as dicts. Keys use `snake_case` and are automatically converted to `--kebab-case` CLI flags:

```stash
// { no_cache: true, build_arg: { VER: "1.0" } }
// becomes: --no-cache --build-arg VER=1.0
```

| Value Type | CLI Conversion |
|---|---|
| `true` | `--flag` (bare flag) |
| `false` / `null` | Skipped |
| `string` / `int` | `--flag value` |
| `array` | Repeated: `--flag val1 --flag val2` |

## Error Handling

```stash
let result = docker.containers.run("bad-image:nonexistent");
if (!result.ok) {
    io.println("Failed: " + str.trim(result.stderr));
    io.println("Exit code: " + conv.toStr(result.exitCode));
}
```

Or use Stash's `try` for unexpected failures:

```stash
let result = try docker.containers.inspect("my-container");
if (result is Error) {
    io.println("Error: " + result.message);
}
```

## Requirements

- Docker CLI installed and available on `PATH`
- Docker daemon running (for most operations)
- Docker Compose v2 (for compose module — uses `docker compose` subcommand)

## License

MIT
