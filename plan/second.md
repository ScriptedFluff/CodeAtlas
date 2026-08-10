┌─────────────────────────────────────────────────────────────────────────────┐
│                          DEVELOPER ENVIRONMENT                              │
│                                                                             │
│  Git Workspace / IDE (VS Code, Copilot, Antigravity, Terminal)              │
└──────────────────────────────────────┬──────────────────────────────────────┘
│ MCP Requests over stdio / HTTP
▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                    MCP CODE KNOWLEDGE GRAPH ENGINE                          │
│               (Standalone Compiled Binary in Rust / C#)                     │
│                                                                             │
│  ┌─────────────────────────┐  ┌─────────────────────┐  ┌─────────────────┐  │
│  │ Tree-sitter Parser      │  │ Git Delta Engine    │  │ Storage Router  │  │
│  │ (C#, TS, PS1, HTML)     │  │ (Diff Reconciliation│  │ (Global/Local)  │  │
│  └────────────┬────────────┘  └──────────┬──────────┘  └────────┬────────┘  │
└───────────────┼──────────────────────────┼──────────────────────┼───────────┘
│                          │                      │
▼                          ▼                      ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                            DATA STORAGE TIER                                │
│                                                                             │
│  • Primary DB: SQLite Relational Knowledge Graph (code_graph.db)          │
│  • In-Memory Buffer: Live Uncommitted/Staged File Parser                    │
│  • Storage Modes:                                                           │
│    - Default: %LOCALAPPDATA%/CodeGraph/dbs/<project_hash>.db             │
│    - Local (--store-local): <repo>/.agent-index/code_graph.db           │
│    - Custom (--db-dir <path>): User-specified directory                   │
└─────────────────────────────────────────────────────────────────────────────┘