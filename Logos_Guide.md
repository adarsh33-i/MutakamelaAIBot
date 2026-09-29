# Logos

## Your AI's Memory Across All Coding Tools

---

<br>

## The Problem

Every time you start a new AI coding session:

| Without Memory | What Happens |
|:---------------|:-------------|
| Context is lost | You explain the same things again |
| Mistakes repeat | AI tries approaches that failed last week |
| Tools don't talk | Work in Cursor is invisible to Claude Code |

<br>

## The Solution

**Logos** creates a shared memory vault that all your AI tools can access.

```
    ╭──────────────╮   ╭──────────────╮   ╭──────────────╮
    │  Claude Code │   │    Cursor    │   │   VS Code    │
    ╰──────┬───────╯   ╰──────┬───────╯   ╰──────┬───────╯
           │                  │                  │
           ╰──────────────────┼──────────────────╯
                              │
                       ╭──────┴──────╮
                       │             │
                       │    LOGOS    │
                       │   (vault)   │
                       │             │
                       ╰─────────────╯
```

**One memory. All tools. Every session.**

---

<br>

## Quick Setup

### Step 1: Install

```bash
brew install coder8124/tap/logos-mcp
```

### Step 2: Configure

```bash
logos setup
```

Type `Y` when prompted to wire your tools.

### Step 3: Restart

Close and reopen Claude Code (or other AI tools).

### Step 4: Verify

```bash
logos doctor --integration
```

**You're done!**

---

<br>

## How It Works

### The Session Lifecycle

```
╭─────────────────────────────────────────────────────────────╮
│                                                             │
│   START SESSION                                             │
│   ─────────────                                             │
│   AI calls: resume                                          │
│                                                             │
│   Returns:                                                  │
│   • What you were doing                                     │
│   • What worked                                             │
│   • What failed                                             │
│   • Next step                                               │
│                                                             │
╰─────────────────────────────┬───────────────────────────────╯
                              │
                              ▼
╭─────────────────────────────────────────────────────────────╮
│                                                             │
│   DURING WORK                                               │
│   ───────────                                               │
│                                                             │
│   before_you_try  →  "Was this tried before?"               │
│   remember        →  Save important facts                   │
│   note_progress   →  Quick breadcrumbs                      │
│                                                             │
╰─────────────────────────────┬───────────────────────────────╯
                              │
                              ▼
╭─────────────────────────────────────────────────────────────╮
│                                                             │
│   END SESSION                                               │
│   ───────────                                               │
│   AI calls: checkpoint                                      │
│                                                             │
│   Saves:                                                    │
│   • verified  →  What actually worked                       │
│   • failed    →  What didn't work (most valuable!)          │
│   • next      →  Single next step                           │
│                                                             │
╰─────────────────────────────────────────────────────────────╯
```

---

<br>

## Real Example

### Monday: First Attempt

```
You:  "Add Redis for caching"

AI:   Tries Redis...
      ❌ Fails on cluster mode

AI:   checkpoint(
        failed: ["Redis broke on cluster mode"]
      )
```

### Wednesday: New Session

```
You:  "Let's add caching"

AI:   before_you_try("use Redis")

Logos: ⚠️  "This failed Monday — broke on cluster mode"

AI:   "Redis was tried before. Suggesting Memcached instead..."
```

**Result: Hours saved by not repeating the mistake!**

---

<br>

## The Tools

### Starting a Session

| Tool | Purpose |
|:-----|:--------|
| `resume` | Get full context from last session |
| `context` | Get context for a specific task |

### During Work

| Tool | Purpose |
|:-----|:--------|
| `before_you_try` | Check if approach was tried & failed |
| `why` | Understand why code is written a certain way |
| `remember` | Save a durable fact |
| `note_progress` | Quick one-line breadcrumb |

### Ending a Session

| Tool | Purpose |
|:-----|:--------|
| `checkpoint` | Save session state |
| `handoff` | Pass work to another tool/person |

### Memory Management

| Tool | Purpose |
|:-----|:--------|
| `recall` | Search memories |
| `list_memories` | Show all memories |
| `forget` | Delete a memory |

---

<br>

## The Checkpoint

The most important part of Logos is the **checkpoint** — what gets saved when a session ends.

```
╭─────────────────────────────────────────────────────────────╮
│                                                             │
│   CHECKPOINT FIELDS                                         │
│                                                             │
│   ┌─────────────┬───────────────────────────────────────┐   │
│   │  verified   │  What was PROVEN to work              │   │
│   │             │  (with the command that proved it)    │   │
│   ├─────────────┼───────────────────────────────────────┤   │
│   │  failed     │  What DIDN'T work and WHY             │   │
│   │             │  ⭐ Most valuable field!              │   │
│   ├─────────────┼───────────────────────────────────────┤   │
│   │  decisions  │  Choices made and reasoning           │   │
│   ├─────────────┼───────────────────────────────────────┤   │
│   │  blockers   │  Known broken/incomplete items        │   │
│   ├─────────────┼───────────────────────────────────────┤   │
│   │  next       │  Single next step                     │   │
│   └─────────────┴───────────────────────────────────────┘   │
│                                                             │
╰─────────────────────────────────────────────────────────────╯
```

### Why "failed" is Most Valuable

The `failed` field prevents the most expensive mistake in software development: **repeating work that was already done.**

---

<br>

## Terminal Commands

### Setup & Health

```bash
# Initial setup
logos setup

# Check health
logos doctor --integration

# View status
logos status
```

### Import History

```bash
# Preview what would be imported
logos ingest --dry-run

# Actually import
logos ingest
```

### Direct Usage

```bash
# View last checkpoint
logos resume <project>

# Check for dead ends
logos before-you-try "your approach"
```

---

<br>

## Vault Location

Your Logos vault is stored at:

```
~/logos/
├── sessions/           ← Checkpoints by project
│   ├── project-a/
│   └── project-b/
├── memory/             ← Stored facts & preferences
└── index/              ← Search indexes
```

---

<br>

## Supported Tools

| Tool | Status |
|:-----|:-------|
| Claude Code | ✅ Full support |
| Claude Desktop | ✅ Full support |
| Cursor | ✅ Full support + session hooks |
| VS Code Copilot | ✅ Full support |
| Cline | ✅ Supported |
| Devin | ✅ Supported |
| Others | Check `logos setup` |

---

<br>

## Quick Reference Card

```
╭─────────────────────────────────────────────────────────────╮
│                                                             │
│   LOGOS QUICK REFERENCE                                     │
│                                                             │
│   ┌───────────────────┬─────────────────────────────────┐   │
│   │  SESSION START    │  resume(project)                │   │
│   ├───────────────────┼─────────────────────────────────┤   │
│   │  BEFORE TRYING    │  before_you_try(approach)       │   │
│   ├───────────────────┼─────────────────────────────────┤   │
│   │  SAVE A FACT      │  remember(text)                 │   │
│   ├───────────────────┼─────────────────────────────────┤   │
│   │  QUICK NOTE       │  note_progress(text)            │   │
│   ├───────────────────┼─────────────────────────────────┤   │
│   │  SESSION END      │  checkpoint(verified, failed)   │   │
│   ├───────────────────┼─────────────────────────────────┤   │
│   │  SWITCH TOOLS     │  handoff(to, ...)               │   │
│   └───────────────────┴─────────────────────────────────┘   │
│                                                             │
╰─────────────────────────────────────────────────────────────╯
```

---

<br>

## Key Principles

1. **Read before you start** — Call `resume` at session start
2. **Check before you try** — Call `before_you_try` before committing to an approach  
3. **Write as you go** — Don't save everything for the end
4. **Record failures** — The `failed` field is the most valuable
5. **Be specific** — Include commands and evidence in `verified`

---

<br>

## One Picture Summary

```
╭─────────────────────────────────────────────────────────────╮
│                                                             │
│         ┌─────────┐                                         │
│         │  START  │                                         │
│         └────┬────┘                                         │
│              │                                              │
│              ▼                                              │
│     ┌────────────────┐                                      │
│     │    resume()    │  ← "What happened last time?"        │
│     └────────┬───────┘                                      │
│              │                                              │
│              ▼                                              │
│     ┌────────────────┐                                      │
│     │     WORK       │                                      │
│     │                │                                      │
│     │  before_you_try  ← "Did this fail before?"            │
│     │  remember        ← Save facts                         │
│     │  note_progress   ← Breadcrumbs                        │
│     │                │                                      │
│     └────────┬───────┘                                      │
│              │                                              │
│              ▼                                              │
│     ┌────────────────┐                                      │
│     │  checkpoint()  │  ← Save verified, failed, next       │
│     └────────┬───────┘                                      │
│              │                                              │
│              ▼                                              │
│     ┌────────────────┐                                      │
│     │  NEXT SESSION  │  ← Starts with full context!         │
│     └────────────────┘                                      │
│                                                             │
╰─────────────────────────────────────────────────────────────╯
```

---

<br>

## Getting Help

```bash
# Check installation
logos doctor

# View all commands
logos --help

# Update Logos
brew upgrade logos-mcp
```

---

<br>

<div align="center">

**Logos** — *Memory for AI Coding Tools*

Made with care for developers who value their time.

</div>
