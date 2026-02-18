import { useState, useRef, useEffect } from "react";
import { useGetTags } from "@/api/generated";
import { TagBadge } from "./TagBadge";

interface TagInputProps {
  value: string[];
  onChange: (tags: string[]) => void;
}

export function TagInput({ value, onChange }: TagInputProps) {
  const [input, setInput] = useState("");
  const [showSuggestions, setShowSuggestions] = useState(false);
  const [selectedIndex, setSelectedIndex] = useState(0);
  const inputRef = useRef<HTMLInputElement>(null);
  const containerRef = useRef<HTMLDivElement>(null);

  const { data: suggestions = [] } = useGetTags(
    { Search: input, IncludeChildren: false },
    { query: { enabled: input.length > 0 } },
  );

  const filteredSuggestions = suggestions.filter(
    (s) => !value.includes(s.path),
  );

  useEffect(() => {
    setSelectedIndex(0);
  }, [input]);

  useEffect(() => {
    const handleClickOutside = (e: MouseEvent) => {
      if (
        containerRef.current &&
        !containerRef.current.contains(e.target as Node)
      ) {
        setShowSuggestions(false);
      }
    };
    document.addEventListener("mousedown", handleClickOutside);
    return () => document.removeEventListener("mousedown", handleClickOutside);
  }, []);

  const addTag = (path: string) => {
    const normalized = path.trim().toLowerCase();
    if (normalized && !value.includes(normalized)) {
      onChange([...value, normalized]);
    }
    setInput("");
    setShowSuggestions(false);
    inputRef.current?.focus();
  };

  const removeTag = (path: string) => {
    onChange(value.filter((t) => t !== path));
  };

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === "Enter") {
      e.preventDefault();
      if (showSuggestions && filteredSuggestions.length > 0 && filteredSuggestions[selectedIndex]) {
        addTag(filteredSuggestions[selectedIndex]!.path);
      } else if (input.trim()) {
        addTag(input);
      }
    } else if (
      e.key === "Backspace" &&
      input === "" &&
      value.length > 0
    ) {
      removeTag(value[value.length - 1]!);
    } else if (e.key === "ArrowDown" && showSuggestions) {
      e.preventDefault();
      setSelectedIndex((i) =>
        i < filteredSuggestions.length - 1 ? i + 1 : i,
      );
    } else if (e.key === "ArrowUp" && showSuggestions) {
      e.preventDefault();
      setSelectedIndex((i) => (i > 0 ? i - 1 : 0));
    } else if (e.key === "Escape") {
      setShowSuggestions(false);
    }
  };

  return (
    <div ref={containerRef} className="relative">
      <div className="flex flex-wrap items-center gap-1.5 rounded-lg border border-border bg-surface px-2 py-1.5 transition-colors focus-within:border-accent-500 focus-within:ring-2 focus-within:ring-accent-500/20">
        {value.map((tag) => (
          <TagBadge key={tag} path={tag} onRemove={() => removeTag(tag)} />
        ))}
        <input
          ref={inputRef}
          value={input}
          onChange={(e) => {
            setInput(e.target.value);
            setShowSuggestions(true);
          }}
          onFocus={() => input && setShowSuggestions(true)}
          onKeyDown={handleKeyDown}
          placeholder={value.length === 0 ? "Add tags..." : ""}
          className="min-w-[80px] flex-1 bg-transparent text-sm text-text-primary placeholder:text-text-muted outline-none"
        />
      </div>

      {showSuggestions && filteredSuggestions.length > 0 && (
        <div className="absolute z-50 mt-1 w-full rounded-lg border border-border bg-surface-overlay shadow-lg">
          {filteredSuggestions.map((suggestion, i) => (
            <button
              key={suggestion.id}
              type="button"
              onClick={() => addTag(suggestion.path)}
              className={`w-full px-3 py-2 text-left text-sm transition-colors ${
                i === selectedIndex
                  ? "bg-accent-50 text-accent-700 dark:bg-accent-950 dark:text-accent-300"
                  : "text-text-primary hover:bg-surface-raised"
              } ${i === 0 ? "rounded-t-lg" : ""} ${i === filteredSuggestions.length - 1 ? "rounded-b-lg" : ""}`}
            >
              {suggestion.path}
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
