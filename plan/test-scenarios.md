The 4 Key Test Scenarios
To get a comprehensive picture, run these 4 query types against both environments:

Scenario A: The Single-Function Surgical Edit
Query: "Add a parameter timeout_sec = 30 to the Get-UsageStats function."

What to watch: Does the control group ingest the entire 700KB script, while the MCP group requests only the ~30-line Get-UsageStats AST slice?

Scenario B: Cross-File Caller Identification
Query: "If I change the signature of CalculateTax in C#, which Angular components or ServiceStack services in clevernpm break?"

What to watch: Control AI will crawl directory structures and search raw strings across node_modules / clevernpm. MCP AI will perform a single indexed relational join across the symbol_references table.

Scenario C: Git Diff Impact Analysis
Query: "I just modified UserService.cs. What downstream methods in my C# API need unit test updates based on my recent git changes?"

What to watch: MCP AI uses get_diff_impact to check last_commit_hash against the local working diff, pointing directly to impacted caller nodes.

Scenario D: Uncommitted Local Buffer Interception (Dirty File Test)
Query: "Review the new method I just started typing in TaxService.cs (uncommitted/unstaged)."

What to watch: Verifies that your MCP server falls back to reading git status --porcelain and parsing the live buffer in memory, rather than returning stale data from the SQLite index.