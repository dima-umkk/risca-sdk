# Risc-A Instruction Set Architecture

16-bit fixed-length instructions, 32-bit data bus (two instructions per word), little-endian byte order.

## Registers

16 general-purpose registers R0–R15 (32-bit each).

## Instruction encoding overview

| Opcode | Type | Format | Operations |
|--------|------|--------|------------|
| 0 | ALU REG REG | `unused(2) \| func3(3) \| Rs(4) \| Rd(4) \| opcode(3)` | MOV, ADD, SUB, AND, OR, XOR, NOT, MUL |
| 1 | ALU Imm | `Imm(7) \| func2(2) \| Rd(4) \| opcode(3)` | SHL, SHR, ADD, SUB |
| 2 | REG Imm | `Imm(8) \| func1(1) \| Rd(4) \| opcode(3)` | MOVI, MOVL |
| 3 | ST/LD | `Imm(3) \| func22(1) \| func21(1) \| Rs(4) \| Rd(4) \| opcode(3)` | LDB, LDW, STB, STW |
| 4 | BRANCH | `Imm(7) \| func2(2) \| Rd(4) \| opcode(3)` | BEQZ, BNEZ, BGTZ, BLTZ |
| 5 | LDI | `Imm(9) \| Rd(4) \| opcode(3)` | Load PC-relative 32-bit constant |
| 6 | CALL/JMP/RET | `Imm(7) \| func2(2) \| Rd(4) \| opcode(3)` | CALL, JMP, RET, JR |
| 7 | INT/RETI/STS | `unused(7) \| func2(2) \| Rd(4) \| opcode(3)` | INT, RETI, MOV EPC, MOV STS |

---

## ALU REG REG — Opcode 0

`unused(2) | func3(3) | Rs(4) | Rd(4) | 000`

```
bits 15–14: unused (must be 0)
bits 13–11: func3  — selects operation
bits 10–7:  Rs     — source register
bits 6–3:   Rd     — destination register
bits 2–0:   000    — opcode
```

| func3 | Mnemonic | Semantics |
|-------|----------|-----------|
| 000 | MOV Rd, Rs | Rd = Rs |
| 001 | ADD Rd, Rs | Rd = Rd + Rs |
| 010 | SUB Rd, Rs | Rd = Rd − Rs |
| 011 | AND Rd, Rs | Rd = Rd & Rs |
| 100 | OR Rd, Rs | Rd = Rd \| Rs |
| 101 | XOR Rd, Rs | Rd = Rd ^ Rs |
| 110 | NOT Rd, Rs | Rd = ~Rs |
| 111 | MUL Rd, Rs | Rd = Rd × Rs |

**Limitations**
- Rd, Rs: 0–15 (4-bit register index).
- `MOV R0, R0` encodes as `0x0000` and is the canonical NOP.
- Unused bits 15–14 must be zero for forward compatibility.

**Example**

| Assembly | Encoding |
|----------|----------|
| `MOV R1, R2` | `0x00C1` |
| `ADD R3, R4` | `0x1243` |
| `SUB R0, R0` | `0x0800` |

---

## ALU Imm — Opcode 1

`Imm(7) | func2(2) | Rd(4) | 001`

```
bits 15–9: Imm(7) — immediate value
bits 8–7:  func2  — selects operation
bits 6–3:  Rd     — destination register
bits 2–0:  001    — opcode
```

| func2 | Mnemonic | Semantics |
|-------|----------|-----------|
| 00 | SHL Rd, Imm(4) | Rd = Rd << (Imm & 0xF) |
| 01 | SHR Rd, Imm(4) | Rd = Rd >> (Imm & 0xF), logical (zero-fill) |
| 10 | ADD Rd, Imm(7) | Rd = Rd + Imm |
| 11 | SUB Rd, Imm(7) | Rd = Rd − Imm |

**Limitations**
- Rd: 0–15.
- SHL/SHR: only the lower 4 bits of Imm are used as the shift amount (0–15).
- ADD/SUB: Imm is a 7-bit unsigned value in range 0–127.
- SHR is a logical shift (zero-filled on the left).

**Example**

| Assembly | Encoding |
|----------|----------|
| `SHL R1, 4` | `0x0221` |
| `ADD R0, 127` | `0xFE01` |
| `SUB R2, 1` | `0x8242` |

---

## REG Imm — Opcode 2

`Imm(8) | func1(1) | Rd(4) | 010`

```
bits 15–8: Imm(8) — immediate value
bit  7:    func1  — selects operation
bits 6–3:  Rd     — destination register
bits 2–0:  010    — opcode
```

| func1 | Mnemonic | Semantics |
|-------|----------|-----------|
| 0 | MOVI Rd, Imm | Rd = Imm (zero-extended) |
| 1 | MOVL Rd, Imm | Rd = (Rd << 8) \| Imm |

**Limitations**
- Rd: 0–15.
- Imm: 8-bit unsigned value (0–255).
- `MOVI` alone covers 0–255. Add `MOVL` to extend to 16, 24, or 32 bits.
  A full 32-bit load: `MOVI Rd, b3` → `MOVL Rd, b2` → `MOVL Rd, b1` → `MOVL Rd, b0`.

**Example**

| Assembly | Encoding |
|----------|----------|
| `MOVI R0, 0xFF` | `0xFF02` |
| `MOVL R0, 0xAA` | `0xFF42` |

Loading 0x12345678 into R1:

```
MOVI R1, 0x12     ; R1 = 0x00000012
MOVL R1, 0x34     ; R1 = 0x00001234
MOVL R1, 0x56     ; R1 = 0x00123456
MOVL R1, 0x78     ; R1 = 0x12345678
```

---

## ST/LD — Opcode 3

`Imm(3) | func22(1) | func21(1) | Rs(4) | Rd(4) | 011`

```
bits 15–13: Imm(3) — unsigned offset
bit  12:    func22 — size: 0 = byte, 1 = word
bit  11:    func21 — direction: 0 = load, 1 = store
bits 10–7:  Rs     — base address register
bits 6–3:   Rd     — data register (load destination / store source)
bits 2–0:   011    — opcode
```

| func21 | func22 | Mnemonic | Semantics |
|--------|--------|----------|-----------|
| 0 | 0 | LDB Rd, [Rs + Imm(3)] | Rd = byte at [Rs + offset] (zero-extended) |
| 0 | 1 | LDW Rd, [Rs + Imm(3)<<2] | Rd = word at [Rs + offset × 4] |
| 1 | 0 | STB [Rs + Imm(3)], Rd | byte at [Rs + offset] = Rd (low byte) |
| 1 | 1 | STW [Rs + Imm(3)<<2], Rd | word at [Rs + offset × 4] = Rd |

**Limitations**
- Rd, Rs: 0–15.
- Imm(3): 3-bit unsigned offset.
- Byte access: offset range 0–7 bytes.
- Word access: offset is Imm(3) << 2, giving range 0, 4, 8, … , 28 (step 4).

**Example**

| Assembly | Encoding |
|----------|----------|
| `LDW R1, [R2 + 0]` | `0x00B3` |
| `STW R4, [R5 + 8]` | `0xA8D3` |
| `LDB R0, [R1 + 3]` | `0x60A3` |

---

## BRANCH — Opcode 4

`Imm(7) | func2(2) | Rd(4) | 100`

```
bits 15–9: Imm(7) — signed offset
bits 8–7:  func2  — branch condition
bits 6–3:  Rd     — compared register
bits 2–0:  100    — opcode
```

| func2 | Mnemonic | Condition |
|-------|----------|-----------|
| 00 | BEQZ Rd, Imm | Rd == 0 |
| 01 | BNEZ Rd, Imm | Rd != 0 |
| 10 | BGTZ Rd, Imm | Rd > 0 (signed) |
| 11 | BLTZ Rd, Imm | Rd < 0 (signed) |

**Limitations**
- Rd: 0–15.
- Imm(7): 7-bit signed value in instruction units (−64 … +63).
- PC-relative offset (bytes) = sign-extended Imm × 2.
- Branch range: −64 … +63 instructions from PC.

**Example**

| Assembly | Encoding |
|----------|----------|
| `BEQZ R0, 4` | `0x1004` |
| `BNEZ R1, −2` | `0x3F24` |

---

## LDI — Opcode 5

`Imm(9) | Rd(4) | 101`

```
bits 15–7: Imm(9) — signed offset
bits 6–3:  Rd     — destination register
bits 2–0:  101    — opcode
```

**Semantics**

`LDI Rd, offset` loads a 32-bit value stored at `PC + sign_ext(Imm) × 2` into Rd. The constant pool is typically placed at function boundaries.

**Limitations**
- Rd: 0–15.
- Imm(9): 9-bit signed value in instruction units (−256 … +255).
- PC-relative offset (bytes) = sign-extended Imm × 2.
- Load range: −256 … +255 instructions from PC.
- Constants must be 2-byte aligned within the instruction stream.

**Example**

| Assembly | Encoding |
|----------|----------|
| `LDI R0, 8` | `0x0425` |
| `LDI R1, −4` | `0x3E2D` |

---

## CALL / JMP / RET — Opcode 6

`Imm(7) | func2(2) | Rd(4) | 110`

```
bits 15–9: Imm(7) — offset (upper 7 bits of 11-bit target)
bits 8–7:  func2  — operation
bits 6–3:  Rd     — lower 4 bits of target (CALL IMM / JR) or target register
bits 2–0:  110    — opcode
```

| func2 | Mnemonic | Semantics |
|-------|----------|-----------|
| 00 | CALL Imm | `R14 = PC + 2`; `PC += sign_ext(Imm[7]\|Rd[4]) × 4` |
| 01 | CALL Rd | `R14 = PC + 2`; `PC = Rd` |
| 10 | RET Rd | `PC = Rd` (aliased JMP Rd; default link register is R14) |
| 11 | JR Imm | `PC += sign_ext(Imm[7]\|Rd[4]) × 2` |

**Limitations**
- Rd: 0–15.
- CALL IMM / JR: target is Imm(7) concatenated with Rd(4) = 11-bit signed value in instruction units (−1024 … +1023).
- CALL IMM: offset × 4 bytes; range −4096 … +4092 bytes. Target must be 4-byte aligned.
- JR: offset × 2 bytes; range −1024 … +1023 instructions from PC.
- CALL Rd / RET: any register value — no range limit.

**Example**

| Assembly | Encoding |
|----------|----------|
| `CALL 16` | `0x20E6` |
| `CALL R14` | `0x00F6` |
| `RET` | `0x01D6` |
| `JR 4` | `0x20F6` |

---

## INT / RETI / STS — Opcode 7

`unused(7) | func2(2) | Rd(4) | 111`

```
bits 15–9: unused (must be 0)
bits 8–7:  func2  — operation
bits 6–3:  Rd     — interrupt number or source/target register for EPC/STS
bits 2–0:  111    — opcode
```

| func2 | Mnemonic | Semantics |
|-------|----------|-----------|
| 00 | INT Rd | Trigger software interrupt; Rd selects interrupt number (0–15) |
| 01 | RETI | Return from interrupt |
| 10 | MOV Rd, EPC | Rd = EPC (read EPC) |
| 11 | MOV EPC, Rd | EPC = Rd (write EPC) |

**Limitations**
- Rd: 0–15.
- Upper 7 bits unused (must be zero).
- EPC: captures `PC + 2` on interrupt entry.

**Interrupt vectors**

| Address | Source |
|---------|--------|
| 0x00000000 | Reset |
| 0x00000002 | Timer |
| 0x00000004 | Syscall (INT Rd) |
| 0x00000006 | … |

**System status (STS) register**

- Bit 0: IE (interrupt enable).
- Bit 1: Priv (1 = kernel mode, 0 = user mode).

---

## ABI

| Role | Register |
|------|----------|
| Arguments | R0–R5 |
| Return value | R0 |
| Link register | R14 (callee must save if re-entrant) |
| Stack pointer | R15 |
| Callee-saved | R8–R13 (must be preserved) |

**Stack push / pop**

```
; PUSH Rd
SUB  R15, 4
STW  [R15], Rd

; POP Rd
LDW  Rd, [R15]
ADD  R15, 4
```

**Syscall convention**

`INT R0` invokes a system function. R1–R5 carry arguments, result returned in R0.

**Constant pools**

Constants are placed after the final `RET` of a function, or before the function body preceded by a `JR` skip. The constant pool must be reachable via LDI's 9-bit signed offset.
