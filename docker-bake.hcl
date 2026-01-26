variable "REGISTRY" {
  default = "ghcr.io"
}

variable "REPO" {
  default = "fl0w1nd/ts3audiobot"
}

variable "TAG" {
  default = "latest"
}

group "default" {
  targets = ["app"]
}

target "app" {
  dockerfile = "Dockerfile"
  context    = "."
  tags = [
    "${REGISTRY}/${REPO}:${TAG}"
  ]
  platforms = ["linux/amd64"]
  cache-from = ["type=gha"]
  cache-to   = ["type=gha,mode=max"]
}

target "local" {
  inherits = ["app"]
  platforms = []
  output = ["type=docker"]
  cache-from = ["type=local,src=/tmp/.buildx-cache"]
  cache-to   = ["type=local,dest=/tmp/.buildx-cache-new,mode=max"]
}

target "dev" {
  inherits = ["local"]
  tags     = ["ts3audiobot:dev"]
  output   = ["type=docker"]
}
