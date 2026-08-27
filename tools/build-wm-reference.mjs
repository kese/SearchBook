import fs from "node:fs/promises";
import { SpreadsheetFile, Workbook } from "@oai/artifact-tool";

const [inputPath, outputPath, previewPath] = process.argv.slice(2);
if (!inputPath || !outputPath || !previewPath) {
  throw new Error("Usage: node build-wm-reference.mjs <input.json> <output.xlsx> <preview.png>");
}

const sourceRows = JSON.parse(await fs.readFile(inputPath, "utf8"));
const headers = [
  "선택", "등록번호", "콘텐츠번호", "자료명", "저자", "출판사", "출판년", "청구기호",
  "소장분관", "소장서고", "자료상태", "비고", "처리결과", "보존서고 등록",
];
const values = [headers, ...sourceRows.map((row) => headers.map((header) => String(row[header] ?? "")))];

const workbook = Workbook.create();
const sheet = workbook.worksheets.add("Sheet1");
sheet.showGridLines = false;
const usedRange = sheet.getRangeByIndexes(0, 0, values.length, headers.length);
usedRange.values = values;
usedRange.format = {
  font: { name: "Malgun Gothic", size: 10, color: "#161616" },
  verticalAlignment: "center",
};
sheet.getRange("A1:N1").format = {
  fill: "#DDE8ED",
  font: { name: "Malgun Gothic", size: 10, bold: true, color: "#344F5F" },
  verticalAlignment: "center",
  wrapText: false,
  borders: { preset: "outside", style: "thin", color: "#BFCED6" },
};
sheet.getRange(`A2:N${values.length}`).format.wrapText = false;
sheet.getRange("B2:B" + values.length).format.numberFormat = "@";
sheet.getRange("C2:C" + values.length).format.numberFormat = "@";
sheet.getRange("A1:N" + values.length).format.rowHeight = 18;
sheet.getRange("A1:N1").format.rowHeight = 24;
const widths = [8, 16, 14, 56, 28, 28, 12, 22, 16, 16, 14, 16, 14, 16];
for (let index = 0; index < widths.length; index += 1) {
  const column = String.fromCharCode("A".charCodeAt(0) + index);
  sheet.getRange(`${column}1:${column}${values.length}`).format.columnWidth = widths[index];
}
sheet.freezePanes.freezeRows(1);

await fs.mkdir(new URL(".", `file:///${previewPath.replaceAll("\\", "/")}`), { recursive: true }).catch(() => {});
const preview = await workbook.render({ sheetName: "Sheet1", range: "A1:N12", scale: 1, format: "png" });
await fs.writeFile(previewPath, new Uint8Array(await preview.arrayBuffer()));
const output = await SpreadsheetFile.exportXlsx(workbook);
await output.save(outputPath);
console.log(JSON.stringify({ rows: sourceRows.length, outputPath, previewPath }));
