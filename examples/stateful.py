if message.content != "!count":
    return

global counter

try:
    counter += 1
except NameError:
    counter = 1

await message.reply(str(counter))

# this should reliably always reply with 1
