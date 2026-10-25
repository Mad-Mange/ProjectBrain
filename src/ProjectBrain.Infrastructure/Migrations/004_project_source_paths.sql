-- Logical project keys can be manifest paths when multiple Unreal projects share a folder.
-- Evidence paths are always relative to source_relative_path (the physical directory).
ALTER TABLE projects ADD COLUMN source_relative_path TEXT;
