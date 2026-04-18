vim.bo.commentstring = "// %s"
vim.bo.shiftwidth = 4
vim.bo.expandtab = true
vim.bo.tabstop = 4

vim.lsp.start({
  name = "stash-lsp",
  cmd = { "stash-lsp" },
  root_dir = vim.fs.root(0, { ".git", "stash.toml" }) or vim.fn.fnamemodify(vim.api.nvim_buf_get_name(0), ":h"),
  filetypes = { "stash" },
})
