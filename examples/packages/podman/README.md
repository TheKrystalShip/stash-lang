# @stash/podman

A comprehensive Podman CLI wrapper for [Stash](https://github.com/stash-lang/stash), providing idiomatic access to the full Podman CLI from Stash scripts. Covers 12 modules and 120+ functions including Podman-specific features like pods, machines, Kubernetes generation, manifests, and secrets.

## Installation

```bash
stash pkg install @stash/podman
```

## Quick Start

```stash
import "@stash/podman" as podman;

// Check Podman is available
if (!podman.system.available()) {
    io.println("Podman is not installed or not in PATH.");
    exit(1);
}

// Run a container
let result = podman.containers.run("nginx:latest", {
    name: "my-nginx",
    detach: true,
    publish: ["8080:80"],
});

if (result.ok) {
    io.println("Container started: " + str.trim(result.stdout));
}

// List running containers
let ps = podman.containers.ls();
if (ps.ok) {
    for (let c in ps.data) {
        io.println($"{c.Names} — {c.Status}");
    }
}

// Stop and remove
podman.containers.stop("my-nginx");
podman.containers.rm("my-nginx");
```

You can also import individual modules directly:

```stash
import "lib/containers.stash" as containers;
import "lib/pods.stash" as pods;
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
| `top(container)` | List running processes in a container |
| `stats(containers, opts)` | Live resource usage (one-shot by default) |
| `diff(container)` | Show filesystem changes since creation |
| `port(container, private_port)` | Show port mappings |
| `container_exec(container, cmd, opts)` | Execute a command in a running container |
| `cp(src, dst)` | Copy files between container and host |
| `commit(container, repo, opts)` | Create an image from a container |
| `export_container(container, output_path)` | Export container filesystem as tar |
| `update(container, opts)` | Update container resource limits |
| `prune(opts)` | Remove stopped containers |

#### Common Options for `run`

```stash
podman.containers.run("ubuntu:22.04", {
    name: "dev-box",
    detach: true,
    env: { NODE_ENV: "production", PORT: "3000" },
    publish: ["3000:3000", "9229:9229"],
    volume: ["./app:/app"],
    label: { team: "backend" },
    network: "my-net",
    restart: "unless-stopped",
    memory: "512m",
    cpus: "1.5",
    user: "node",
    workdir: "/app",
    pod: "my-pod",
    command: "node server.js",
});
```

### images

Image management — 15 functions.

| Function | Description |
|---|---|
| `ls(opts)` | List local images (JSON) |
| `pull(image, opts)` | Pull an image from a registry |
| `push(image, opts)` | Push an image to a registry |
| `build(path, opts)` | Build an image from a Containerfile or Dockerfile |
| `tag(source, target)` | Tag an image |
| `rmi(image, opts)` | Remove an image |
| `inspect(image)` | Get detailed image info (JSON) |
| `history(image, opts)` | Show image layers (JSON) |
| `save(images, output_path)` | Save images to a tar archive |
| `load(input_path, opts)` | Load images from a tar archive |
| `import_image(source, repo, opts)` | Import a tarball as an image |
| `prune(opts)` | Remove unused images |
| `search(term, opts)` | Search configured registries (JSON) |
| `tree(image, opts)` | Show layer tree of an image (Podman-specific) |

#### Build Example

```stash
podman.images.build(".", {
    tag: "myapp:latest",
    file: "Containerfile.prod",
    build_arg: { VERSION: "1.2.3" },
    no_cache: true,
    platform: "linux/amd64",
});
```

### volumes

Volume management — 7 functions.

| Function | Description |
|---|---|
| `create(name, opts)` | Create a named volume |
| `rm(volume, opts)` | Remove a volume |
| `ls(opts)` | List volumes (JSON) |
| `inspect(volume)` | Get volume details (JSON) |
| `prune(opts)` | Remove unused volumes |
| `exists(volume)` | Check whether a volume exists (Podman-specific) |

### networks

Network management — 9 functions.

| Function | Description |
|---|---|
| `create(name, opts)` | Create a network |
| `rm(network)` | Remove a network |
| `ls(opts)` | List networks (JSON) |
| `inspect(network)` | Get network details (JSON) |
| `connect(network, container, opts)` | Connect a container to a network |
| `disconnect(network, container, opts)` | Disconnect a container from a network |
| `prune(opts)` | Remove unused networks |
| `exists(network)` | Check whether a network exists (Podman-specific) |

### pods

Podman-native pod management — 15 functions. Pods are a Podman-specific feature that groups containers sharing the same network namespace, analogous to Kubernetes pods.

| Function | Description |
|---|---|
| `create(opts)` | Create a new pod |
| `start(pod, opts)` | Start a pod and all its containers |
| `stop(pod, opts)` | Stop a pod and all its containers |
| `restart(pod, opts)` | Restart a pod |
| `pause(pod)` | Pause a pod |
| `unpause(pod)` | Unpause a pod |
| `kill(pod, opts)` | Send a signal to all containers in a pod |
| `rm(pod, opts)` | Remove a pod |
| `ls(opts)` | List pods (JSON) |
| `inspect(pod)` | Get detailed pod info (JSON) |
| `top(pod)` | List running processes in a pod |
| `stats(pod, opts)` | Resource usage for containers in a pod |
| `prune(opts)` | Remove stopped pods |
| `exists(pod)` | Check whether a pod exists |
| `clone(pod, opts)` | Clone an existing pod with optional overrides |

#### Pods Example

```stash
// Create a pod with a shared published port
let pod_result = podman.pods.create({
    name: "web-pod",
    publish: ["8080:80"],
});

if (pod_result.ok) {
    io.println("Pod created: " + str.trim(pod_result.stdout));
}

// Run containers inside the pod — they share the pod's network
podman.containers.run("nginx:latest", {
    pod: "web-pod",
    name: "web-server",
    detach: true,
});

podman.containers.run("busybox:latest", {
    pod: "web-pod",
    name: "sidecar",
    detach: true,
    command: "sh -c 'while true; do sleep 30; done'",
});

// Inspect the pod
let info = podman.pods.inspect("web-pod");
if (info.ok) {
    io.println($"Pod status: {info.data.State}");
    io.println($"Containers: {conv.toStr(info.data.NumContainers)}");
}

// List all pods
let pods = podman.pods.ls();
for (let p in pods.data) {
    io.println($"{p.Name} — {p.Status} ({p.NumContainers} containers)");
}

// Tear down
podman.pods.stop("web-pod");
podman.pods.rm("web-pod");
```

### compose

Podman Compose wrapper — 22 functions.

| Function | Description |
|---|---|
| `up(opts)` | Create and start services |
| `down(opts)` | Stop and remove services |
| `build_services(opts)` | Build service images |
| `ps(opts)` | List service containers (JSON) |
| `logs(opts)` | View service logs |
| `compose_exec(service, cmd, opts)` | Execute a command in a running service |
| `run(service, cmd, opts)` | Run a one-off command against a service |
| `restart(opts)` | Restart services |
| `stop(opts)` | Stop services |
| `start(opts)` | Start services |
| `pull(opts)` | Pull service images |
| `push(opts)` | Push service images |
| `kill(opts)` | Force stop services |
| `rm(opts)` | Remove stopped services |
| `config(opts)` | Validate and view the resolved compose config |
| `top(opts)` | Display running processes per service |
| `images(opts)` | List images used by services |
| `port(service, port, opts)` | Show public port for a service port |
| `pause(opts)` | Pause services |
| `unpause(opts)` | Unpause services |
| `create(opts)` | Create services without starting |
| `cp(src, dst, opts)` | Copy files to/from service containers |

All compose functions accept project-level options: `file` (string or array of `-f` paths), `project_name` (string for `-p`), `project_directory`, `env_file`, `profile`.

#### Compose Example

```stash
// Start a project
podman.compose.up({
    file: "compose.prod.yml",
    project_name: "myapp",
    detach: true,
    build: true,
});

// Check running services
let services = podman.compose.ps({ project_name: "myapp" });
if (services.ok) {
    for (let svc in services.data) {
        io.println($"{svc.Name}: {svc.State}");
    }
}

// View logs for a specific service
podman.compose.logs({
    project_name: "myapp",
    service: "api",
    tail: "50",
    follow: true,
});

// Tear down
podman.compose.down({
    project_name: "myapp",
    volumes: true,
    remove_orphans: true,
});
```

### system

System-level Podman operations — 12 functions.

| Function | Description |
|---|---|
| `info(opts)` | System-wide Podman info (JSON) |
| `version(opts)` | Client/server version info (JSON) |
| `df(opts)` | Disk usage summary (JSON) |
| `ping()` | Check if the Podman service is responsive |
| `events(opts)` | Stream Podman events |
| `prune(opts)` | Remove all unused data |
| `available()` | Check if Podman CLI is installed and in PATH |
| `reset(opts)` | Reset Podman storage to its initial state |
| `migrate(opts)` | Migrate containers after a Podman upgrade |
| `renumber(opts)` | Renumber lock numbers for containers/pods |
| `connection_list(opts)` | List remote Podman connections (JSON) |

### registry

Registry authentication — 3 functions.

| Function | Description |
|---|---|
| `login(server, opts)` | Log in to a container registry |
| `logout(server)` | Log out from a container registry |
| `search(term, opts)` | Search configured registries (JSON) |

### machine

Podman Machine management for running Podman in a VM (macOS/Windows) — 11 functions.

| Function | Description |
|---|---|
| `init(opts)` | Initialize a new Podman machine VM |
| `start(opts)` | Start a Podman machine |
| `stop(opts)` | Stop a Podman machine |
| `rm(name, opts)` | Remove a Podman machine |
| `ls(opts)` | List Podman machines (JSON) |
| `inspect(name)` | Get detailed machine info (JSON) |
| `ssh(name, opts)` | SSH into a Podman machine |
| `set(name, opts)` | Modify machine settings |
| `reset(opts)` | Reset all Podman machine state |
| `info(opts)` | Show Podman machine provider info (JSON) |
| `os_apply(name, opts)` | Apply an OS layer to a Podman machine |

#### Machine Example

```stash
// Initialize a Podman machine with custom resources
podman.machine.init({
    cpus: 4,
    memory: 4096,
    disk_size: 50,
    now: true,          // start immediately after init
});

// Or start an existing machine
podman.machine.start();

// List machines and show their status
let machines = podman.machine.ls();
if (machines.ok) {
    for (let m in machines.data) {
        io.println($"{m.Name} — {m.LastUp} (running: {conv.toStr(m.Running)})");
    }
}

// SSH into the default machine
podman.machine.ssh("podman-machine-default", {
    command: "uname -r",
});

// Stop and clean up
podman.machine.stop();
podman.machine.rm("podman-machine-default", { force: true });
```

### generate

Kubernetes and systemd artifact generation — 8 functions. These are Podman-specific functions for generating integration artifacts from running containers and pods.

| Function | Description |
|---|---|
| `kube(name, opts)` | Generate Kubernetes YAML from a pod or container |
| `systemd(name, opts)` | Generate systemd unit files for a container or pod |
| `spec(name, opts)` | Generate an OCI spec (JSON) for a container |
| `play_kube(file, opts)` | Create pods/containers from a Kubernetes YAML |
| `play_kube_down(file, opts)` | Remove pods/containers defined by a Kubernetes YAML |
| `quadlet(opts)` | Generate Quadlet systemd unit files |
| `kube_from_pod(pod, opts)` | Generate Kubernetes YAML specifically from a pod |
| `systemd_from_container(container, opts)` | Generate systemd unit file for a container |

#### Kubernetes Integration Example

```stash
// Generate Kubernetes YAML from a running pod
let yaml = podman.generate.kube("web-pod", { service: true });
io.println(yaml.stdout);

// Write the YAML to a file for use with kubectl
// (using Stash's file I/O)
fs.write("web-pod.yaml", yaml.stdout);

// Play a Kubernetes YAML file — creates pods and containers
podman.generate.play_kube("deployment.yaml", {
    network: "my-network",
    log_driver: "journald",
});

// Generate a systemd unit file for a container
let unit = podman.generate.systemd("my-nginx", {
    new: true,
    restart_policy: "always",
    name: true,
});
io.println(unit.stdout);

// Tear down resources defined in a Kubernetes YAML
podman.generate.play_kube_down("deployment.yaml");
```

### manifest

OCI manifest list management for multi-architecture images — 8 functions.

| Function | Description |
|---|---|
| `create(name, opts)` | Create a manifest list |
| `add(list, image, opts)` | Add an image to a manifest list |
| `remove(list, digest)` | Remove a manifest entry by digest |
| `inspect(name)` | Inspect a manifest list (JSON) |
| `push(name, destination, opts)` | Push a manifest list to a registry |
| `rm(name)` | Remove a manifest list |
| `annotate(list, image, opts)` | Annotate a manifest entry |
| `exists(name)` | Check whether a manifest list exists |

### secret

Podman secret management — 5 functions.

| Function | Description |
|---|---|
| `create(name, opts)` | Create a new secret |
| `inspect(name)` | Get secret metadata (JSON; value is never returned) |
| `ls(opts)` | List secrets (JSON) |
| `rm(name)` | Remove a secret |
| `exists(name)` | Check whether a secret exists |

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
let result = podman.containers.run("bad-image:nonexistent");
if (!result.ok) {
    io.println("Failed: " + str.trim(result.stderr));
    io.println("Exit code: " + conv.toStr(result.exitCode));
}
```

Or use Stash's `try` for unexpected failures:

```stash
let result = try podman.containers.inspect("my-container");
if (result is Error) {
    io.println("Error: " + result.message);
}
```

## Podman vs Docker

`@stash/podman` and `@stash/docker` share a similar API surface because Podman is largely CLI-compatible with Docker. Key differences:

| Feature | Podman | Docker |
|---|---|---|
| Daemon | Daemonless — each command forks directly | Requires a running `dockerd` daemon |
| Rootless | First-class rootless container support | Rootless mode is an opt-in add-on |
| Pods | Native pod support (this package) | Not available |
| Kubernetes | `generate kube`, `play kube`, Quadlet | Not available |
| Machine | `podman machine` for VM-based workflows | `docker context` / Docker Desktop |
| Manifests | Native manifest list management | `docker manifest` (experimental) |
| Secrets | Local secret store | Requires Swarm mode |
| Compose | Via `podman-compose` or `docker-compose` | Via `docker compose` (v2 plugin) |

Because Podman aims to be a drop-in Docker replacement, many options and behaviours are identical. If you are migrating scripts from `@stash/docker`, the main changes are: using `publish` instead of `ports` for port mappings, and taking advantage of Podman-only modules such as `pods`, `machine`, `generate`, `manifest`, and `secret`.

## Requirements

- Podman CLI installed and available on `PATH`
- For `compose` module: `podman-compose` or `docker-compose` installed and available on `PATH`
- For `machine` module: Podman Machine supported on your OS (macOS or Windows)

## License

MIT
