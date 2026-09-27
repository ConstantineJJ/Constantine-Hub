from __future__ import annotations

from mcp.server import MCPServer

from .config import load_settings
from .service import LocalFilesService

mcp = MCPServer("Constantine Local Files MCP")
_service = LocalFilesService(load_settings())


@mcp.tool()
def files_server_info() -> dict:
    """Return Local Files MCP version, config path, audit path, and capabilities."""
    return _service.server_info()


@mcp.tool()
def files_roots() -> dict:
    """List configured filesystem roots and their read/write/delete permissions."""
    return _service.roots()


@mcp.tool()
def files_exists(path: str) -> dict:
    """Check whether an allowlisted absolute path exists."""
    return _service.exists(path)


@mcp.tool()
def files_stat(path: str) -> dict:
    """Return metadata for an existing allowlisted file or directory."""
    return _service.stat(path)


@mcp.tool()
def files_list(
    path: str,
    recursive: bool = False,
    max_depth: int = 2,
    pattern: str = "*",
) -> dict:
    """List a directory. Recursive traversal never follows symlinks/junctions."""
    return _service.list_directory(
        path, recursive=recursive, max_depth=max_depth, pattern=pattern
    )


@mcp.tool()
def files_read_text(
    path: str,
    start_line: int = 1,
    end_line: int | None = None,
) -> dict:
    """Read UTF-8 text, optionally limited to an inclusive 1-based line range."""
    return _service.read_text(path, start_line=start_line, end_line=end_line)


@mcp.tool()
def files_search_names(
    root: str,
    query: str,
    max_results: int | None = None,
) -> dict:
    """Search names below an allowlisted directory using a case-insensitive substring."""
    return _service.search_names(root, query, max_results=max_results)


@mcp.tool()
def files_search_text(
    root: str,
    query: str,
    file_glob: str = "*",
    case_sensitive: bool = False,
    max_results: int | None = None,
) -> dict:
    """Search UTF-8 files below a root without following symlinks/junctions."""
    return _service.search_text(
        root,
        query,
        file_glob=file_glob,
        case_sensitive=case_sensitive,
        max_results=max_results,
    )


@mcp.tool()
def files_create_directory(
    path: str,
    parents: bool = True,
    exist_ok: bool = False,
) -> dict:
    """Create an allowlisted directory and record the mutation in the audit log."""
    return _service.create_directory(path, parents=parents, exist_ok=exist_ok)


@mcp.tool()
def files_write_text(
    path: str,
    content: str,
    overwrite: bool = False,
    create_parents: bool = False,
    backup: bool = False,
) -> dict:
    """Create or atomically overwrite a UTF-8 file inside an allowlisted writable root."""
    return _service.write_text(
        path,
        content,
        overwrite=overwrite,
        create_parents=create_parents,
        backup=backup,
    )


@mcp.tool()
def files_edit_text(
    path: str,
    old_text: str,
    new_text: str,
    expected_replacements: int = 1,
    backup: bool = True,
) -> dict:
    """Replace exact text only when the expected occurrence count matches."""
    return _service.edit_text(
        path,
        old_text,
        new_text,
        expected_replacements=expected_replacements,
        backup=backup,
    )


@mcp.tool()
def files_append_text(path: str, content: str) -> dict:
    """Append UTF-8 text to an existing writable file."""
    return _service.append_text(path, content)


@mcp.tool()
def files_copy(source: str, destination: str, overwrite: bool = False) -> dict:
    """Copy a file or a reparse-free directory tree between allowlisted paths."""
    return _service.copy(source, destination, overwrite=overwrite)


@mcp.tool()
def files_move(source: str, destination: str, overwrite: bool = False) -> dict:
    """Move or rename an allowlisted path; source must also permit delete."""
    return _service.move(source, destination, overwrite=overwrite)


@mcp.tool()
def files_delete(path: str, recursive: bool = False) -> dict:
    """Delete an allowlisted file or directory. Recursive directory deletion is explicit."""
    return _service.delete(path, recursive=recursive)


def main() -> None:
    mcp.run()


if __name__ == "__main__":
    main()
