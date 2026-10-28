// Runs scripts/search_encoded_strings.py against the current program's loaded memory.
//@author HackThePlanet
//@category HackThePlanet
//@menupath Tools.HackThePlanet.Search Encoded Strings
//@keybinding
//@toolbar

import generic.jar.ResourceFile;

import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.BookmarkType;
import ghidra.program.model.address.AddressSet;
import ghidra.program.model.address.AddressSetView;
import ghidra.program.model.mem.Memory;
import ghidra.program.model.mem.MemoryBlock;
import java.io.BufferedReader;
import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStreamReader;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.util.ArrayList;
import java.util.List;

public class SearchEncodedStrings extends GhidraScript {

	private static final long MAX_EXPORT_BYTES = 512L * 1024L * 1024L;

	private static class Hit {
		final long address;
		final long dumpOffset;
		final int length;
		final String encodings;
		final String preview;

		Hit(long address, long dumpOffset, int length, String encodings, String preview) {
			this.address = address;
			this.dumpOffset = dumpOffset;
			this.length = length;
			this.encodings = encodings;
			this.preview = preview;
		}

		@Override
		public String toString() {
			String enc = encodings.length() > 60 ? encodings.substring(0, 57) + "..." : encodings;
			return String.format("%#x  len=%d  [%s]  %s", address, length, enc, preview);
		}
	}

	@Override
	protected void run() throws Exception {
		if (currentProgram == null) {
			popup("Open a program first.");
			return;
		}

		String query = askString("Search Encoded Strings", "Word or phrase to find:", "");
		if (query == null || query.trim().isEmpty()) {
			return;
		}
		query = query.trim();

		boolean selectionOnly = false;
		if (currentSelection != null && !currentSelection.isEmpty()) {
			selectionOnly = askYesNo(
				"Search Encoded Strings",
				"Limit search to the current selection?");
		}

		boolean ignoreCase = askYesNo("Search Encoded Strings", "Ignore case?");
		boolean allCharsets = askYesNo(
			"Search Encoded Strings",
			"Search all Python codec aliases? (slow — default charset set is usually enough)");
		String charsets = "";
		if (askYesNo(
			"Search Encoded Strings",
			"Limit to specific charsets? (No = built-in default set)")) {
			charsets = askString(
				"Search Encoded Strings",
				"Comma-separated charsets (e.g. gbk,utf-8,utf-16-le):",
				"utf-8");
			if (charsets == null) {
				return;
			}
			charsets = charsets.trim();
		}

		File pythonScript = locatePythonScript();
		File pythonExe = locatePythonExecutable();

		File tempBin = File.createTempFile("htp-encoded-search-", ".bin");
		File tempMap = File.createTempFile("htp-encoded-search-", ".json");
		tempBin.deleteOnExit();
		tempMap.deleteOnExit();

		monitor.setMessage("Exporting memory...");
		long exported = exportMemory(tempBin, tempMap, selectionOnly);
		if (exported == 0) {
			popup("No initialized memory to search.");
			return;
		}
		if (exported > MAX_EXPORT_BYTES) {
			popup("Memory export exceeds 512 MiB; narrow the selection and retry.");
			return;
		}

		List<String> command = new ArrayList<>();
		command.add(pythonExe.getAbsolutePath());
		if (pythonExe.getName().equalsIgnoreCase("py")) {
			command.add("-3");
		}
		command.add(pythonScript.getAbsolutePath());
		command.add(tempBin.getAbsolutePath());
		command.add(query);
		command.add("--region-map");
		command.add(tempMap.getAbsolutePath());
		command.add("--ghidra-csv");
		if (ignoreCase) {
			command.add("-i");
		}
		if (allCharsets) {
			command.add("--all-charsets");
		}
		if (charsets != null && !charsets.isEmpty()) {
			command.add("--charsets");
			command.add(charsets);
		}

		monitor.setMessage("Searching charsets...");
		List<Hit> hits = runPython(command);
		if (hits.isEmpty()) {
			popup("No matches for \"" + query + "\".");
			return;
		}

		for (Hit hit : hits) {
			Address addr = toAddress(hit.address);
			if (addr != null) {
				currentProgram.getBookmarkManager().setBookmark(
					addr,
					BookmarkType.NOTE,
					"EncodedString",
					hit.encodings + " " + hit.preview);
			}
		}

		List<String> choices = new ArrayList<>(hits.size());
		for (Hit hit : hits) {
			choices.add(hit.toString());
		}

		println("Search Encoded Strings — " + hits.size() + " hit(s). Bookmarks added.");
		for (String line : choices) {
			println(line);
		}

		String picked = askChoice(
			"Search Encoded Strings — " + hits.size() + " hit(s)",
			"Go to address:",
			choices,
			choices.get(0));
		if (picked != null) {
			int idx = choices.indexOf(picked);
			if (idx >= 0) {
				Address addr = toAddress(hits.get(idx).address);
				if (addr != null) {
					goTo(addr);
				}
			}
		}
	}

	private Address toAddress(long offset) {
		return currentProgram.getAddressFactory().getDefaultAddressSpace().getAddress(offset);
	}

	private File locatePythonScript() throws Exception {
		String root = System.getenv("HACKTHEPLANET_ROOT");
		if (root != null && !root.isEmpty()) {
			File fromEnv = new File(root, "scripts/search_encoded_strings.py");
			if (fromEnv.isFile()) {
				return fromEnv.getCanonicalFile();
			}
		}

		ResourceFile source = getSourceFile();
		if (source != null) {
			File scriptJava = source.getFile(false);
			if (scriptJava == null) {
				scriptJava = source.getFile(true);
			}
			if (scriptJava == null) {
				scriptJava = new File(source.getAbsolutePath());
			}
			File sourceDir = scriptJava.getParentFile();
			if (sourceDir != null) {
				File scriptsDir = sourceDir.getParentFile();
				if (scriptsDir != null) {
					File sibling = new File(scriptsDir, "search_encoded_strings.py");
					if (sibling.isFile()) {
						return sibling.getCanonicalFile();
					}
				}
			}
		}

		throw new Exception(
			"Could not find search_encoded_strings.py. Set HACKTHEPLANET_ROOT to your repo root " +
				"(e.g. I:\\HackThePlanet\\HackThePlanet).");
	}

	private File locatePythonExecutable() throws Exception {
		File fromConfig = readConfiguredPython();
		if (fromConfig != null) {
			return fromConfig;
		}

		String explicit = System.getenv("HACKTHEPLANET_PYTHON");
		if (explicit != null && !explicit.isEmpty()) {
			File py = new File(explicit);
			if (py.isFile()) {
				return py.getCanonicalFile();
			}
		}

		for (File candidate : discoverPythonCandidates()) {
			if (candidate.isFile()) {
				return candidate.getCanonicalFile();
			}
		}

		throw new Exception(
			"Python 3 not found. Ghidra often starts without Python on PATH.\n" +
				"Set user environment variable HACKTHEPLANET_PYTHON to your python.exe, e.g.\n" +
				"  C:\\Program Files\\Python313\\python.exe\n" +
				"Then restart Ghidra.\n" +
				"Or create scripts/ghidra/SearchEncodedStrings.python with one line: the full path to python.exe.");
	}

	/** Optional one-line path file next to this script (not committed). */
	private File readConfiguredPython() throws Exception {
		ResourceFile source = getSourceFile();
		if (source == null) {
			return null;
		}
		File scriptJava = source.getFile(false);
		if (scriptJava == null) {
			scriptJava = source.getFile(true);
		}
		if (scriptJava == null) {
			scriptJava = new File(source.getAbsolutePath());
		}
		File config = new File(scriptJava.getParentFile(), "SearchEncodedStrings.python");
		if (!config.isFile()) {
			return null;
		}
		String line = Files.readString(config.toPath(), StandardCharsets.UTF_8).trim();
		if (line.isEmpty() || line.startsWith("#")) {
			return null;
		}
		File py = new File(line);
		if (!py.isFile()) {
			throw new Exception("SearchEncodedStrings.python points to missing file: " + line);
		}
		return py.getCanonicalFile();
	}

	private List<File> discoverPythonCandidates() {
		List<File> found = new ArrayList<>();
		String[] pathNames = isWindows()
			? new String[] { "py", "python", "python3" }
			: new String[] { "python3", "python" };
		for (String name : pathNames) {
			addIfFile(found, findOnPath(name));
		}
		if (isWindows()) {
			collectWindowsInstalls(found);
		}
		return found;
	}

	private void collectWindowsInstalls(List<File> found) {
		collectPythonCoreInstalls(found, new File(System.getenv("LOCALAPPDATA"), "Python"));
		collectPythonExeInRoot(found, new File(System.getenv("LOCALAPPDATA"), "Programs/Python"));
		collectPythonExeInRoot(found, new File(System.getenv("ProgramFiles")));
		addIfFile(found, new File(System.getenv("LOCALAPPDATA"), "Microsoft/WindowsApps/py.exe"));
		addIfFile(found, new File(System.getenv("LOCALAPPDATA"), "Microsoft/WindowsApps/python.exe"));
		String programFilesX86 = System.getenv("ProgramFiles(x86)");
		if (programFilesX86 != null) {
			collectPythonExeInRoot(found, new File(programFilesX86));
		}
		String userProfile = System.getenv("USERPROFILE");
		if (userProfile != null) {
			collectPythonExeInRoot(
				found, new File(userProfile, "AppData/Local/Programs/Python"));
		}
	}

	private void collectPythonExeInRoot(List<File> found, File root) {
		if (root == null || !root.isDirectory()) {
			return;
		}
		File[] children = root.listFiles();
		if (children == null) {
			return;
		}
		for (File child : children) {
			if (!child.isDirectory()) {
				continue;
			}
			String name = child.getName().toLowerCase();
			if (!name.startsWith("python")) {
				continue;
			}
			addIfFile(found, new File(child, "python.exe"));
		}
	}

	/** Python install manager: %LOCALAPPDATA%\\Python\\pythoncore-3.x-64\\python.exe */
	private void collectPythonCoreInstalls(List<File> found, File root) {
		if (root == null || !root.isDirectory()) {
			return;
		}
		File[] children = root.listFiles();
		if (children == null) {
			return;
		}
		for (File child : children) {
			if (!child.isDirectory()) {
				continue;
			}
			if (!child.getName().toLowerCase().startsWith("pythoncore-")) {
				continue;
			}
			addIfFile(found, new File(child, "python.exe"));
		}
	}

	private void addIfFile(List<File> found, File file) {
		if (file != null && file.isFile() && !found.contains(file)) {
			found.add(file);
		}
	}

	private boolean isWindows() {
		return System.getProperty("os.name", "").toLowerCase().contains("win");
	}

	private File findOnPath(String name) {
		String path = System.getenv("PATH");
		if (path == null) {
			return null;
		}
		String[] parts = path.split(File.pathSeparator);
		for (String dir : parts) {
			File exe = new File(dir, isWindows() ? name + ".exe" : name);
			if (exe.isFile()) {
				return exe;
			}
			File bare = new File(dir, name);
			if (bare.isFile()) {
				return bare;
			}
		}
		return null;
	}

	private long exportMemory(File tempBin, File tempMap, boolean selectionOnly)
			throws Exception {
		Memory memory = currentProgram.getMemory();
		AddressSetView selection = currentSelection;
		StringBuilder json = new StringBuilder();
		json.append("{\"regions\":[");
		boolean firstRegion = true;
		long dumpOffset = 0;
		long total = 0;

		try (FileOutputStream out = new FileOutputStream(tempBin)) {
			for (MemoryBlock block : memory.getBlocks()) {
				if (!block.isInitialized()) {
					continue;
				}
				if (selectionOnly && selection != null && !selection.isEmpty()) {
					AddressSet blockSet = new AddressSet(block.getStart(), block.getEnd());
					if (!selection.intersects(blockSet)) {
						continue;
					}
				}

				long blockSize = block.getSize();
				if (blockSize <= 0) {
					continue;
				}
				if (blockSize > Integer.MAX_VALUE) {
					printerr("Skipping block " + block.getName() + " (>2 GiB).");
					continue;
				}

				byte[] data = new byte[(int) blockSize];
				int read = memory.getBytes(block.getStart(), data);
				if (read <= 0) {
					continue;
				}
				if (read != data.length) {
					byte[] trimmed = new byte[read];
					System.arraycopy(data, 0, trimmed, 0, read);
					data = trimmed;
				}

				out.write(data);
				if (!firstRegion) {
					json.append(',');
				}
				firstRegion = false;
				json.append("{\"dump_offset\":")
					.append(dumpOffset)
					.append(",\"address\":")
					.append(block.getStart().getOffset())
					.append(",\"length\":")
					.append(data.length)
					.append('}');

				dumpOffset += data.length;
				total += data.length;
				if (monitor.isCancelled()) {
					break;
				}
			}
		}

		json.append("]}");
		Files.write(tempMap.toPath(), json.toString().getBytes(StandardCharsets.UTF_8));
		return total;
	}

	private List<Hit> runPython(List<String> command) throws Exception {
		ProcessBuilder builder = new ProcessBuilder(command);
		builder.redirectErrorStream(true);
		Process process = builder.start();

		List<Hit> hits = new ArrayList<>();
		try (BufferedReader reader =
				new BufferedReader(new InputStreamReader(process.getInputStream(), StandardCharsets.UTF_8))) {
			String line;
			boolean header = true;
			while ((line = reader.readLine()) != null) {
				line = line.trim();
				if (line.isEmpty()) {
					continue;
				}
				if (header) {
					header = false;
					continue;
				}
				if (line.startsWith("address,")) {
					continue;
				}
				Hit hit = parseCsvLine(line);
				if (hit != null) {
					hits.add(hit);
				} else if (line.contains("hit(s)") || line.startsWith("warning:")) {
					println(line);
				}
			}
		}

		int code = process.waitFor();
		if (code != 0 && hits.isEmpty()) {
			throw new Exception("Python search failed (exit " + code + "). See console output.");
		}
		return hits;
	}

	private Hit parseCsvLine(String line) {
		// address,dump_offset,length,encodings,preview  (encodings/preview may be quoted)
		List<String> fields = splitCsv(line);
		if (fields.size() < 5) {
			return null;
		}
		try {
			long address = parseHexLong(fields.get(0));
			long dumpOffset = Long.parseLong(fields.get(1));
			int length = Integer.parseInt(fields.get(2));
			return new Hit(address, dumpOffset, length, fields.get(3), fields.get(4));
		}
		catch (NumberFormatException e) {
			return null;
		}
	}

	private long parseHexLong(String text) {
		text = text.trim();
		if (text.startsWith("0x") || text.startsWith("0X")) {
			return Long.parseUnsignedLong(text.substring(2), 16);
		}
		return Long.parseUnsignedLong(text);
	}

	private List<String> splitCsv(String line) {
		List<String> out = new ArrayList<>();
		StringBuilder current = new StringBuilder();
		boolean inQuotes = false;
		for (int i = 0; i < line.length(); i++) {
			char c = line.charAt(i);
			if (inQuotes) {
				if (c == '"') {
					if (i + 1 < line.length() && line.charAt(i + 1) == '"') {
						current.append('"');
						i++;
					}
					else {
						inQuotes = false;
					}
				}
				else {
					current.append(c);
				}
			}
			else if (c == '"') {
				inQuotes = true;
			}
			else if (c == ',') {
				out.add(current.toString());
				current.setLength(0);
			}
			else {
				current.append(c);
			}
		}
		out.add(current.toString());
		return out;
	}
}
