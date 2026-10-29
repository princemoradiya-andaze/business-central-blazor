export function load(key) {
    const value = localStorage.getItem(key);
    if (value !== null && value.length > 250000) {
        throw new Error("Saved analysis preferences exceed the supported size.");
    }
    return value;
}

export function save(key, json) {
    if (json.length > 250000) {
        throw new Error("Analysis preferences exceed the supported size. Remove unused views or filters.");
    }
    localStorage.setItem(key, json);
}
