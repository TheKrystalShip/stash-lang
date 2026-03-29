# @stash/oci

Shared OCI container runtime operations for Stash. This package provides the common foundation layer for OCI-compatible container tools, including `@stash/docker` and `@stash/podman`.

## Overview

`@stash/oci` is not intended to be used directly. Instead, it serves as the shared infrastructure that `@stash/docker` and `@stash/podman` build upon. It defines six common modules — containers, images, volumes, networks, registry, and system — each exposing a `create_module(helpers)` factory that binds operations to a specific OCI tool binary at runtime.

Tool-specific features (compose, context, pods, machine, generate, manifest, secret, etc.) remain implemented in their respective packages and are not part of this shared layer.

## Modules

| Module       | Description                                      |
| ------------ | ------------------------------------------------ |
| `containers` | Run, stop, remove, inspect, exec, logs, and more |
| `images`     | Pull, push, build, tag, inspect, list, prune     |
| `volumes`    | Create, remove, inspect, list, prune             |
| `networks`   | Create, remove, inspect, list, connect           |
| `registry`   | Login, logout, search                            |
| `system`     | Info, version, events, df, prune                 |

## Usage

Tool-specific packages use `create_helpers()` from `lib/common.stash` to bind all shared modules to a particular OCI binary:

```stash
import "@stash/oci/lib/common.stash" as oci_common;
import "@stash/oci/lib/containers.stash" as oci_containers;
import "@stash/oci/lib/images.stash" as oci_images;

// Create helpers bound to "docker" (or "podman", etc.)
let helpers = oci_common.create_helpers("docker");

// Create tool-specific module instances
let containers = oci_containers.create_module(helpers);
let images = oci_images.create_module(helpers);

// Use them
containers.run("nginx:latest", { name: "web", detach: true });
images.pull("alpine:3.18");
```

A full wrapper package typically creates all modules and re-exports them as a unified dict:

```stash
fn create_docker() {
    let helpers = oci_common.create_helpers("docker");
    return {
        containers: oci_containers.create_module(helpers),
        images:     oci_images.create_module(helpers),
        volumes:    oci_volumes.create_module(helpers),
        networks:   oci_networks.create_module(helpers),
        registry:   oci_registry.create_module(helpers),
        system:     oci_system.create_module(helpers),
    };
}
```

## Installation

```sh
stash install @stash/oci
```

This package is typically installed automatically as a dependency of `@stash/docker` or `@stash/podman`.

## License

MIT © 2025 TheKrystalShip
