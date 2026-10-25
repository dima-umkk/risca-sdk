## Risc-A CPU. 
Simple RISC architecture aimed at running several instructions in parallel (IMEM, MEM, ALU, BRANCH).

Byte order: Little-Endian.

Bus is 32 bit, Instructions 16 bit. 2 instructions in 32 bit. Constants alignment for LDI is always 2 bytes (in instructions). Function alignment is 4 bytes (for CALL)

General purpose registers 32 bit: R0-R15

## Instruction table

| Opcode (3 bits) | Type        | Format (16 bit)                                  | Description |
| --------------- | ----------- | ------------------------------------------------ | ----------- |
| 0               | [ALU REG REG](#alu-reg-reg) | ```unused(1)        func4(4)  Rs(4) Rd(4) opcode(3)``` | MOV,ADD,SUB,AND,OR,XOR,NOT,MUL,SLT,SLTU|
| 1               | [ALU Imm](#alu-imm)         | ```Imm(7)           func2(2)        Rd(4) opcode(3)``` | SHR,SHL,ADD,SUB |
| 2               | [REG Imm](#reg-imm)         | ```Imm(8)           func1(1)        Rd(4) opcode(3)``` | MOVI,MOVL |
| 3               | [ST\LD](#stld)              | ```Imm(3) func22(1) func21(1) Rs(4) Rd(4) opcode(3)``` | LDB,LDW,STB,STW |
| 4               | [BRANCH](#branch)           | ```Imm(8)           func1(1)        Rd(4) opcode(3)``` | BEQZ,BNEZ |
| 5               | [LDI](#ldi)                 | ```Imm(9)                           Rd(4) opcode(3)``` | Load indirect 32 bit constant |
| 6               | [CALL\JMP\RET](#calljmpret) | ```Imm(7)           func2(2)        Rd(4) opcode(3)``` | CALL,JMP,RET,JR |
| 7               | [INT\RETI\STS](#intretists) | ```unused(7)        func2(2)        Rd(4) opcode(3)``` | INT,RETI,MOV EPC |



## Instructions

### ALU REG REG:
Opcode: 0

```
unused(1) | func4(4) | Rs(4) | Rd(4) | opcode(3)
```

	NOP is MOV R0, R0 - 0x0000 hex
	func4:

	0) MOV Rd, Rs; Rd = Rs
	1) ADD Rd, Rs; Rd = Rd + Rs
	2) SUB Rd, Rs; Rd = Rd - Rs
	3) AND Rd, Rs; Rd = Rd and Rs
	4) OR  Rd, Rs; Rd = Rd or  Rs
	5) XOR Rd, Rs; Rd = Rd xor Rs
	6) NOT Rd, Rs; Rd = ~Rs
	7) MUL Rd, Rs; Rd = Rd * Rs
	8) SLT Rd, Rs; Rd = (Rd < Rs) ? 1 : 0 (signed)
	9) SLTU Rd, Rs; Rd = (Rd < Rs) ? 1 : 0 (unsigned)
	...
	unused
	
### ALU Imm:
Opcode: 1

```
Imm(7) | func2(2) | Rd(4) | opcode(3)
```

	func2:

		0) SHL Rd, Imm(4); Rd = Rd << Imm(0-4 bits)
		1) SHR Rd, Imm(4); Rd = Rd >> Imm(0-4 bits); Logical - fills 0 on the left
		2) ADD Rd, Imm(7); Rd = Rd + Imm(7)
		3) SUB Rd, Imm(7); Rd = Rd - Imm(7)

### REG Imm:
Opcode: 2

```
Imm(8) | func1(1) | Rd(4) | opcode(3)
```

	func1:

		0) MOVI Rd, Imm(8); Rd = Imm(8)
		1) MOVL Rd, Imm(8); Rd = (Rd << 8) | Imm(8)

	MOVI and MOVL are used to load constants. Any number (from 8 bit to 32 bit)

### ST\LD:
Opcode: 3

```
Imm(3) | func22(1) | func21(1) |  Rs(4) |  Rd(4) | opcode(3)
```

	For Word access Imm(3)<<2 is used
	func21:

	0) LD Rd, [Rs+Imm(3)]; LDW or LDB 
	1) ST [Rs+Imm(3)], Rd; STW or STB
	func22:
	0) Byte access
	1) Word access (32 bit)

### BRANCH:
Opcode: 4

```
Imm(8) | func1(1) | Rd(4) | opcode(3)
```

	func1:

	0) BEQZ Rd, Imm(8); Rd == 0 ? PC = PC + signed(Imm)<<1; in instructions (-128 .. +127 instructions)
	1) BNEZ Rd, Imm(8); Rd != 0 ? PC = PC + signed(Imm)<<1; in instructions (-128 .. +127 instructions)

### LDI:
Opcode: 5

```
Imm(9) | Rd(4) | opcode(3)
```

	LDI Rd, [PC+Signed(Imm(9))<<1]; Imm in instructions (-256 .. +255 instructions)

### CALL\JMP\RET:
Opcode: 6

```
Imm(7) | func2(2) | Rd(4) | opcode(3)
```

	func2:

	0) CALL (Imm(7) | Rd(4)) 11 bit ; PC=PC+signed((Imm(7) | Rd(4)))<<2; Function aligned by 4 bytes; R14=PC+2 return address
	1) CALL Rd         ; PC=Rd. R14=PC+2 return address
	2) RET  Rd         ; PC=Rd. Also used as JMP Rd; By default R14 is used
	3) JR   (Imm(7) | Rd(4)) 11 bit ; PC=PC+signed(Imm(7) | Rd(4))<<1; in instructions

### INT\RETI\STS:
Opcode: 7

```
unused(7) | func2(2) | Rd(4) | opcode(3)
```

	func2:

	0) INT Rd; interrupt number Rd
	1) RETI; return from interrupt
	2) MOV Rd, EPC; Get EPC
	3) MOV EPC, Rd; Set EPC register

	EPC register: the PC of the next instruction to execute after the interrupt handler has finished
	On interrupt EPC = PC + 2
    STS register: bit0=IE (Enable Interrupts), bit1=Priv (User/Kernel)	

	Interrupt vectors:
	0x00000000 - Reset
	0x00000002 - Timer
	0x00000004 - Syscall (INT Rd)
	0x00000006 - ...


## ABI:

	Arguments: R0-R5
	Return: R0
	Stack: R15
	Link Register: R14. The link register should be pushed onto the stack and restored if a subcall occurs
	A function should restore R8-R13 if it uses them
	Constants used in a function are stored at the end of the function after ret, or at the beginning (for near LDI). By default at the end.
	If the constants are at the beginning, each function starts with a JR that skips them. (Constants are always in ICache (IMEM))
	Stack:
		PUSH: 
			SUB R15, 4
			STW [R15 + 0], Rd
		POP:
			LDW Rd, [R15 + 0]
			ADD R15, 4

	Syscall: INT R0 - system function, R1-R5 arguments. Result: R0
