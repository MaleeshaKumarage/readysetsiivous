"""Credential-holding, fixed-provider gateway. Never log prompts/provider errors."""
import json
import os
import threading
import time
import urllib.request
from http.server import BaseHTTPRequestHandler, HTTPServer

MODEL = "deepseek-v4-flash"
ENDPOINT = "https://api.deepseek.com/chat/completions"
MAX_BODY = 2 * 1024 * 1024


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        raise ValueError("redirect rejected")


def provider(payload, secret):
    request = urllib.request.Request(ENDPOINT, data=json.dumps(payload).encode(), headers={
        "Authorization": "Bearer " + secret, "Content-Type": "application/json"})
    with urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect()).open(request, timeout=60) as response:
        raw = response.read(MAX_BODY + 1)
    if len(raw) > MAX_BODY:
        raise ValueError("oversized response")
    return json.loads(raw)


class Gateway:
    def __init__(self, secret, transport=provider, clock=time.monotonic):
        self.secret, self.transport, self.clock = secret, transport, clock
        self.requests, self.last = 0, None
        self.lock = threading.Lock()

    def complete(self, value):
        if not isinstance(value, dict) or set(value) != {"model", "messages", "max_tokens"}:
            raise ValueError("invalid request")
        if value["model"] != MODEL or type(value["max_tokens"]) is not int or not 1 <= value["max_tokens"] <= 4096:
            raise ValueError("invalid request")
        messages = value["messages"]
        if not isinstance(messages, list) or not 1 <= len(messages) <= 16:
            raise ValueError("invalid request")
        for message in messages:
            if not isinstance(message, dict) or set(message) != {"role", "content"} or message["role"] not in ("system", "user", "assistant") or not isinstance(message["content"], str):
                raise ValueError("invalid request")
        if len(json.dumps(value).encode()) > MAX_BODY:
            raise ValueError("invalid request")
        with self.lock:
            now = self.clock()
            if self.requests >= 8 or (self.last is not None and now - self.last < 20):
                raise ValueError("budget exceeded")
            self.requests += 1
            self.last = now
            try:
                result = self.transport(value, self.secret)
                if result["choices"][0].get("finish_reason") != "stop":
                    raise ValueError()
                content = result["choices"][0]["message"]["content"]
                if not isinstance(content, str) or not content.strip() or len(content.encode()) > MAX_BODY:
                    raise ValueError()
                return {"content": content}
            except Exception:
                raise ValueError("provider unavailable") from None


def serve():
    gateway = Gateway(os.environ["DEEPSEEK_API_KEY"])

    class Handler(BaseHTTPRequestHandler):
        def setup(self):
            super().setup()
            # Includes request line/headers and body reads. Single worker bounds
            # connection resource use independently of attacker-controlled clients.
            self.connection.settimeout(10)

        def log_message(self, *args):
            pass

        def do_POST(self):
            try:
                if self.path != "/complete" or self.headers.get("Transfer-Encoding"):
                    raise ValueError()
                size = int(self.headers.get("Content-Length", "0"))
                if not 0 < size <= MAX_BODY:
                    raise ValueError()
                result = gateway.complete(json.loads(self.rfile.read(size)))
                status = 200
            except Exception:
                result, status = {"error": "request rejected"}, 400
            raw = json.dumps(result).encode()
            self.send_response(status)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(raw)))
            self.end_headers()
            self.wfile.write(raw)

    HTTPServer(("0.0.0.0", 8080), Handler).serve_forever()


if __name__ == "__main__":
    serve()
